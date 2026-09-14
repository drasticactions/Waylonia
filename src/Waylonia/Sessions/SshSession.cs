using System.Diagnostics;
using Basin.Freedesktop;
using Waylonia.Audio;
using Waylonia.Ui;
using static Waylonia.WayloniaLog;

namespace Waylonia.Sessions;

internal sealed class SshSession : IDisposable
{
    private const string LoggedIn = "waylonia: logged in";

    private const string RemoteRuntimeDir =
        "if [ -z \"$XDG_RUNTIME_DIR\" ] || [ ! -d \"$XDG_RUNTIME_DIR\" ]; then " +
        "r=/run/user/$(id -u); " +
        "if [ ! -d \"$r\" ]; then r=/tmp/waylonia-run-$(id -u); mkdir -p \"$r\" && chmod 700 \"$r\"; fi; " +
        "XDG_RUNTIME_DIR=$r; export XDG_RUNTIME_DIR; fi; ";

    private static int _counter;

    private readonly ISessionHost _host;
    private readonly string _remoteSocket;
    private readonly string _displayName;
    private readonly string _xDisplayFile;
    private readonly string? _configDir;
    private readonly string _sinkName;
    private readonly string _tag;
    private readonly List<Process> _launched = [];
    private readonly object _gate = new();
    private Process? _ssh;
    private Relay? _relay;
    private TaskCompletionSource? _loggedIn;
    private string? _controlPath;
    private string? _forwardTarget;
    private WaypipeAcceptor? _acceptor;
    private WayloniaAudio? _audio;
    private Task<bool>? _connecting;
    private bool _disconnecting;
    private bool _disposed;

    public SshSession(SessionSettings settings, ISessionHost host)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(host);
        Settings = settings;
        _host = host;
        _tag = $"{Environment.ProcessId}-{Interlocked.Increment(ref _counter)}";
        _remoteSocket = $"/tmp/waylonia-{_tag}.sock";
        _displayName = $"waylonia-{_tag}";
        _xDisplayFile = $"/tmp/waylonia-x-{_tag}";
        _configDir = host.Settings.GtkDpi ? $"/tmp/waylonia-config-{_tag}" : null;
        _sinkName = $"waylonia-{_tag}";
    }

    public SessionSettings Settings { get; private set; }

    public string Name => Settings.Name;

    public string Ssh => Settings.Ssh;

    public SessionStatus Status { get; private set; }

    public string? LastError { get; private set; }

    public string StatusText { get; private set; } = "disconnected";

    public bool IsLive => Status != SessionStatus.Disconnected;

    public bool HadClients => _acceptor is { Attached: > 0 };

    public string RemoteDisplay => _displayName;

    public IReadOnlyList<ApplicationMenuItem>? Applications { get; private set; }

    public string? ApplicationsNotice { get; private set; }

    public WaypipeAcceptor? Acceptor => _acceptor;

    public event Action<SshSession>? Changed;

    public event Action<SshSession, int>? Ended;

    public IReadOnlyList<string> RecentOutput => _relay?.Lines() ?? [];

    public void Replace(SessionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (IsLive)
        {
            throw new InvalidOperationException($"{Name} is connected; disconnect it before changing its settings");
        }

        Settings = settings;
        Changed?.Invoke(this);
    }

    public Task<bool> ConnectAsync()
    {
        lock (_gate)
        {
            if (_connecting is { IsCompleted: false } pending)
            {
                return pending;
            }

            if (Status == SessionStatus.Connected && _ssh is { HasExited: false })
            {
                return Task.FromResult(true);
            }

            _connecting = ConnectCoreAsync();
            return _connecting;
        }
    }

    private async Task<bool> ConnectCoreAsync()
    {
        _disconnecting = false;
        LastError = null;
        SetStatus(SessionStatus.Connecting, $"logging in to {Ssh}");
        if (!StartForward())
        {
            SetStatus(SessionStatus.Disconnected, LastError ?? "ssh could not start");
            return false;
        }

        var ssh = _ssh!;
        if (!await WaitForLoginAsync())
        {
            var exited = ssh.HasExited;
            LastError = exited
                ? $"ssh to {Ssh} exited with {ssh.ExitCode}" + Detail()
                : "the login was interrupted";
            if (exited)
            {
                Log.Error($"{LastError}");
                _relay?.Report();
            }

            SetStatus(SessionStatus.Disconnected, LastError);
            if (!_disconnecting && !_host.ShuttingDown)
            {
                Ended?.Invoke(this, 1);
            }

            return false;
        }

        if (Settings.Audio && _audio is null)
        {
            _audio = WayloniaAudio.TryStart(_host.Audio, Ssh, _controlPath, _sinkName, Settings.AudioFormat);
        }

        SetStatus(SessionStatus.Connected, $"connected to {Ssh}");
        _ = WatchForwardAsync(ssh);
        if (Settings.Desktop is { } recipe)
        {
            var environment = DesktopSession.Environment(recipe, Settings.DesktopEnv, Settings.Gpu);
            var wrapper = DesktopSession.Wrapper(recipe, _displayName, recipe.Command, environment);
            StartRemoteClient(wrapper, exportDisplay: false);
            SetStatus(SessionStatus.Connected, $"starting {recipe.Name} on {Ssh}");
            _ = WatchArrivalAsync(ssh, recipe.Name);
        }
        else
        {
            if (Settings.Command is { } command)
            {
                StartRemoteClient(command);
                _ = WatchArrivalAsync(ssh, command);
            }
            else if (Settings.Autostart.Count == 0)
            {
                Log.Info($"holding the channel to {Ssh} open; clients attach as they start");
                SetStatus(SessionStatus.Connected, $"connected to {Ssh}, waiting for a client");
            }

            foreach (var autostart in Settings.Autostart)
            {
                StartRemoteClient(autostart);
            }

            _ = LoadApplicationsAsync();
        }

        return true;
    }

    private string Detail() => _relay?.LastLine() is { } line ? $": {line}" : string.Empty;

    private async Task WatchArrivalAsync(Process ssh, string what)
    {
        var acceptor = _acceptor!;
        var attachedTask = Task.Run(async () =>
        {
            while (acceptor.Attached == 0 && !_host.ShuttingDown && !_disconnecting)
            {
                await Task.Delay(200);
            }
        });
        await Task.WhenAny(attachedTask, ssh.WaitForExitAsync(), Task.Delay(TimeSpan.FromSeconds(30)));
        if (acceptor.Attached > 0 || _disconnecting || _host.ShuttingDown || !ReferenceEquals(ssh, _ssh))
        {
            return;
        }

        if (ssh.HasExited)
        {
            return;
        }

        LastError = $"no channel arrived from {Ssh} within 30 seconds of starting '{what}'";
        Log.Error($"{LastError}");
        _relay?.Report();
        SetStatus(SessionStatus.Connected, LastError);
        if (Settings.AdHoc)
        {
            await DisconnectAsync();
            Ended?.Invoke(this, 1);
        }
    }

    public async Task<bool> LaunchAsync(string command, string label)
    {
        if (!await EnsureConnectedAsync())
        {
            return false;
        }

        try
        {
            if (StartRemoteClient(command) is null)
            {
                return false;
            }
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Warn($"{label}: '{command}' failed to start on {Ssh}: {error.Message}");
            return false;
        }

        _host.Status($"started '{command}' on {Ssh}");
        return true;
    }

    private async Task<bool> EnsureConnectedAsync()
    {
        if (_ssh is not (null or { HasExited: true }))
        {
            return await WaitForLoginAsync();
        }

        Log.Info($"the connection to {Ssh} is gone; opening it again");
        _host.Status($"reconnecting to {Ssh}");
        return await ConnectAsync();
    }

    public async Task LoadApplicationsAsync()
    {
        if (!_host.Settings.TrayApps || Settings.IsDesktop)
        {
            return;
        }

        var locale = DesktopLocale.FromEnvironment();
        var currentDesktop = ApplicationMenu.CurrentDesktop(Settings.CurrentDesktop);
        var terminal = ApplicationMenu.Terminal(Settings.Terminal);
        ApplicationsNotice = "Loading applications…";
        Changed?.Invoke(this);
        IReadOnlyList<DesktopEntry> listable;
        try
        {
            if (_ssh is null or { HasExited: true } || !await WaitForLoginAsync())
            {
                Notice($"Disconnected from {Ssh}");
                return;
            }

            if (await ReadRemoteApplicationsAsync(locale) is not { } output)
            {
                Notice($"The applications on {Ssh} could not be read");
                return;
            }

            listable = RemoteApplications.Parse(output, locale).Listable(currentDesktop);
        }
        catch (Exception error) when (error is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Warn($"{Name}: the application list could not be read: {error.Message}");
            Notice("The applications could not be read");
            return;
        }

        var items = ApplicationMenu.Build(listable, terminal);
        if (terminal is null && listable.Any(static entry => entry.Terminal))
        {
            Log.Info($"{Name}: terminal applications are left out of the tray menu; set terminal to list them");
        }

        Log.Debug($"{Name}: {listable.Count} application(s) in {items.Count} categor(ies) for the tray menu");
        Applications = items;
        ApplicationsNotice = items.Count == 0 ? "No applications found" : null;
        Changed?.Invoke(this);
    }

    private void Notice(string text)
    {
        Applications = null;
        ApplicationsNotice = text;
        Changed?.Invoke(this);
    }

    private ProcessStartInfo ControlledSsh(bool batch = false)
    {
        var info = new ProcessStartInfo("ssh")
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        if (batch)
        {
            info.ArgumentList.Add("-o");
            info.ArgumentList.Add("BatchMode=yes");
        }

        if (_controlPath is { } control)
        {
            info.ArgumentList.Add("-o");
            info.ArgumentList.Add($"ControlPath={control}");
            info.ArgumentList.Add("-o");
            info.ArgumentList.Add("ControlMaster=no");
        }

        info.ArgumentList.Add(Ssh);
        return info;
    }

    private async Task<string?> ReadRemoteApplicationsAsync(DesktopLocale locale)
    {
        var info = ControlledSsh();
        info.StandardOutputEncoding = System.Text.Encoding.UTF8;
        info.ArgumentList.Add(RemoteApplications.Script(locale));
        using var listing = Process.Start(info);
        if (listing is null)
        {
            return null;
        }

        var relay = new Relay("applications");
        relay.Watch(listing.StandardError);
        var output = listing.StandardOutput.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        try
        {
            await listing.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            Log.Warn($"listing the applications on {Ssh} took over 90 s; giving up");
            listing.Kill();
            return null;
        }

        if (listing.ExitCode != 0)
        {
            Log.Warn($"listing the applications on {Ssh} exited with {listing.ExitCode}");
            relay.Report();
        }

        return await output;
    }

    private string RemoteLang =>
        Settings.Lang is { } lang ? $"if [ -z \"$LANG\" ]; then LANG={lang}; export LANG; fi; " : string.Empty;

    private Process? StartRemoteClient(string command, bool exportDisplay = true)
    {
        if (Status != SessionStatus.Connected)
        {
            Log.Warn($"'{command}' cannot start on {Ssh}: the remote session is not up");
            return null;
        }

        var quoted = command.Replace("'", "'\\''", StringComparison.Ordinal);
        var info = ControlledSsh();
        var pulse = Settings.Audio
            ? $"PULSE_SINK={_sinkName} PIPEWIRE_NODE={_sinkName} "
            : string.Empty;
        var gtkConfig = _configDir is { } configDir
            ? $"if [ -d {configDir} ]; then XDG_CONFIG_HOME={configDir}; export XDG_CONFIG_HOME; fi; "
            : string.Empty;
        var display = exportDisplay
            ? $"if [ -s {_xDisplayFile} ]; then DISPLAY=$(cat {_xDisplayFile}); export DISPLAY; fi; "
            : string.Empty;
        info.ArgumentList.Add(
            RemoteRuntimeDir +
            RemoteLang +
            $"d=\"$XDG_RUNTIME_DIR/{_displayName}\"; i=0; " +
            $"while [ ! -S \"$d\" ] && [ $i -lt 50 ]; do sleep 0.2; i=$((i+1)); done; " +
            display +
            gtkConfig +
            $"{pulse}XDG_SESSION_TYPE=wayland WAYLAND_DISPLAY={_displayName} sh -c '{quoted}'");
        var started = Process.Start(info);
        if (started is null)
        {
            return null;
        }

        lock (_launched)
        {
            _launched.Add(started);
        }

        var relay = new Relay(command);
        relay.Watch(started.StandardOutput);
        relay.Watch(started.StandardError);
        _ = WatchClientAsync(started, relay, command);
        return started;
    }

    private async Task WatchClientAsync(Process client, Relay relay, string command)
    {
        await client.WaitForExitAsync();
        lock (_launched)
        {
            _launched.Remove(client);
        }

        if (_host.ShuttingDown || _disconnecting || client.ExitCode == 0)
        {
            return;
        }

        Log.Warn($"'{command}' exited with {client.ExitCode}");
        relay.Report();
    }

    internal string RemoteScript()
    {
        var compress = Settings.Compression switch
        {
            Basin.Transport.Waypipe.WaypipeCompression.None => "none",
            Basin.Transport.Waypipe.WaypipeCompression.Zstd => "zstd",
            _ => "lz4",
        };
        var gpuArgument = Settings.Gpu ? string.Empty : "--no-gpu ";
        var videoArgument = Settings.Video is { } codec ? $"--video={codec} " : string.Empty;
        var xwayland = _host.Settings.XWayland
            ? "if command -v xwayland-satellite >/dev/null 2>&1; then w=--xwls; else w=; fi; "
              + "export XCURSOR_SIZE=\"${XCURSOR_SIZE:-24}\"; "
            : "w=; ";
        var sink = Settings.Audio
            ? $"m=$(pactl load-module module-null-sink sink_name={_sinkName} " +
              $"sink_properties=device.description=Waylonia 2>/dev/null) || m=; "
            : string.Empty;
        var unloadSink = Settings.Audio ? "[ -n \"$m\" ] && pactl unload-module \"$m\"; " : string.Empty;
        var gtkConfig = _configDir is { } configDir
            ? $"c=\"${{XDG_CONFIG_HOME:-$HOME/.config}}\"; g={configDir}; rm -rf \"$g\"; " +
              "if mkdir -p \"$g\" 2>/dev/null; then " +
              "for e in \"$c\"/* \"$c\"/.[!.]*; do " +
              "if [ -e \"$e\" ]; then ln -s \"$e\" \"$g/${e##*/}\"; fi; done; " +
              "for v in 3.0 4.0; do rm -f \"$g/gtk-$v\"; mkdir -p \"$g/gtk-$v\"; " +
              "for e in \"$c/gtk-$v\"/*; do " +
              "if [ -e \"$e\" ]; then ln -s \"$e\" \"$g/gtk-$v/${e##*/}\"; fi; done; " +
              "if [ -f \"$c/gtk-$v/settings.ini\" ]; then rm -f \"$g/gtk-$v/settings.ini\"; " +
              "sed \"s/^gtk-xft-dpi[[:space:]]*=.*/gtk-xft-dpi=98304/\" " +
              "\"$c/gtk-$v/settings.ini\" > \"$g/gtk-$v/settings.ini\"; fi; done; fi; "
            : string.Empty;
        var removeGtkConfig = _configDir is { } staged ? $"rm -rf {staged}; " : string.Empty;
        return
            $"echo '{LoggedIn}'; " +
            RemoteRuntimeDir +
            RemoteLang +
            $"d=\"$XDG_RUNTIME_DIR/{_displayName}\"; rm -f \"$d\" {_xDisplayFile}; " +
            xwayland +
            gtkConfig +
            sink +
            $"waypipe --compress {compress} {gpuArgument}{videoArgument}--socket {_remoteSocket} " +
            $"--display {_displayName} $w server -- " +
            $"sh -c 'printf %s \"$DISPLAY\" > {_xDisplayFile}; exec cat >/dev/null'; " +
            $"status=$?; rm -f {_remoteSocket} \"$d\" {_xDisplayFile}; " +
            removeGtkConfig +
            unloadSink +
            "exit $status";
    }

    private bool StartForward()
    {
        if (_acceptor is null)
        {
            var acceptor = new WaypipeAcceptor(
                Name, _host, Settings.Compression, Settings.Gpu, Settings.Video, Settings.VideoDecoder, Settings);
            acceptor.Changed += () => Changed?.Invoke(this);
            acceptor.Failed += error =>
            {
                LastError = $"the channel listener failed: {error.Message}";
                SetStatus(Status, LastError);
            };
            System.Net.Sockets.Socket listener;
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    listener = WaypipeAcceptor.Listen(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0));
                    _forwardTarget = $"127.0.0.1:{((System.Net.IPEndPoint)listener.LocalEndPoint!).Port}";
                }
                else
                {
                    var runtimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") ?? Path.GetTempPath();
                    var path = Path.Combine(runtimeDir, $"waylonia-ssh-{_tag}.sock");
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }

                    listener = WaypipeAcceptor.Listen(new System.Net.Sockets.UnixDomainSocketEndPoint(path));
                    _forwardTarget = path;
                }
            }
            catch (Exception error) when (error is System.Net.Sockets.SocketException or IOException or UnauthorizedAccessException)
            {
                LastError = $"the channel listener failed: {error.Message}";
                Log.Error($"{Name}: {LastError}");
                return false;
            }

            _acceptor = acceptor;
            acceptor.Accept(listener);
        }

        var info = new ProcessStartInfo("ssh")
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            RedirectStandardInput = true,
        };
        AskPass.Configure(info.Environment);
        info.ArgumentList.Add("-o");
        info.ArgumentList.Add("StreamLocalBindUnlink=yes");
        info.ArgumentList.Add("-o");
        info.ArgumentList.Add("ExitOnForwardFailure=yes");
        if (!OperatingSystem.IsWindows())
        {
            var controlDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") ?? Path.GetTempPath();
            _controlPath = Path.Combine(controlDir, $"waylonia-ssh-{_tag}.ctl");
            try
            {
                File.Delete(_controlPath);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
            }

            info.ArgumentList.Add("-o");
            info.ArgumentList.Add("ControlMaster=auto");
            info.ArgumentList.Add("-o");
            info.ArgumentList.Add($"ControlPath={_controlPath}");
            info.ArgumentList.Add("-o");
            info.ArgumentList.Add("ControlPersist=no");
        }

        info.ArgumentList.Add("-R");
        info.ArgumentList.Add($"{_remoteSocket}:{_forwardTarget}");
        info.ArgumentList.Add(Ssh);
        info.ArgumentList.Add(RemoteScript());
        Process? ssh;
        try
        {
            ssh = Process.Start(info);
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            LastError = $"ssh could not start: {error.Message}";
            Log.Error($"{LastError}");
            return false;
        }

        if (ssh is null)
        {
            LastError = "ssh could not start";
            Log.Error($"{LastError}");
            return false;
        }

        var loggedIn = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _loggedIn = loggedIn;
        _relay = new Relay("ssh");
        _relay.Watch(ssh.StandardOutput, line => line == LoggedIn && loggedIn.TrySetResult());
        _relay.Watch(ssh.StandardError);
        _ssh = ssh;
        return true;
    }

    private async Task<bool> WaitForLoginAsync()
    {
        if (_ssh is not { } ssh || _loggedIn is not { } loggedIn)
        {
            return false;
        }

        await Task.WhenAny(loggedIn.Task, ssh.WaitForExitAsync());
        return loggedIn.Task.IsCompleted && !ssh.HasExited && !_host.ShuttingDown && !_disconnecting;
    }

    private async Task WatchForwardAsync(Process ssh)
    {
        await ssh.WaitForExitAsync();
        if (_host.ShuttingDown || _disconnecting || !ReferenceEquals(ssh, _ssh))
        {
            return;
        }

        var hadClients = HadClients;
        LastError = ssh.ExitCode == 0
            ? $"the connection to {Ssh} ended"
            : $"ssh to {Ssh} exited with {ssh.ExitCode}" + Detail();
        if (hadClients)
        {
            Log.Info($"the connection to {Ssh} ended; a hotkey or the tray opens it again");
        }
        else
        {
            Log.Error($"{LastError}");
            _relay?.Report();
        }

        ReleaseLocal();
        SetStatus(SessionStatus.Disconnected, $"disconnected from {Ssh}");
        Ended?.Invoke(this, ssh.ExitCode == 0 ? 0 : 1);
    }

    private void ReleaseLocal()
    {
        _audio?.Dispose();
        _audio = null;
        _acceptor?.CloseChannels();
        if (_controlPath is { } control)
        {
            try
            {
                File.Delete(control);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    public bool StopClients()
    {
        Process[] launched;
        lock (_launched)
        {
            launched = [.. _launched];
            _launched.Clear();
        }

        foreach (var client in launched)
        {
            Basin.Diagnostics.BasinDiagnostics.StopClient(client);
        }

        return launched.Length > 0;
    }

    public async Task DisconnectAsync()
    {
        if (_ssh is null && Status == SessionStatus.Disconnected)
        {
            return;
        }

        _disconnecting = true;
        if (StopClients())
        {
            await Task.Delay(100);
        }

        _audio?.Dispose();
        _audio = null;
        if (_ssh is { } ssh)
        {
            await Task.Run(RemoveRemoteSocket);
            if (!ssh.HasExited)
            {
                try
                {
                    ssh.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                }
            }
        }

        ReleaseLocal();
        _ssh = null;
        _loggedIn = null;
        Applications = null;
        ApplicationsNotice = null;
        SetStatus(SessionStatus.Disconnected, $"disconnected from {Ssh}");
    }

    private void RemoveRemoteSocket()
    {
        if (_ssh is null or { HasExited: true })
        {
            return;
        }

        var info = ControlledSsh(batch: true);
        var removeConfig = _configDir is { } configDir ? $"; rm -rf {configDir}" : string.Empty;
        info.ArgumentList.Add(
            RemoteRuntimeDir +
            $"rm -f {_remoteSocket} {_xDisplayFile} \"$XDG_RUNTIME_DIR/{_displayName}\"{removeConfig}");
        try
        {
            using var remove = Process.Start(info);
            if (remove is null)
            {
                return;
            }

            var complaint = remove.StandardError.ReadToEndAsync();
            if (remove.WaitForExit(2000) && remove.ExitCode != 0)
            {
                Log.Debug($"{_remoteSocket} may still exist on {Ssh}: {SshVis.Unescape(complaint.Result.Trim())}");
            }
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Warn($"{_remoteSocket} could not be removed from {Ssh}: {error.Message}");
        }
    }

    private void SetStatus(SessionStatus status, string text)
    {
        Status = status;
        StatusText = text;
        _host.Status($"{Name}: {text}");
        Changed?.Invoke(this);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _disconnecting = true;
        StopClients();
        _audio?.Dispose();
        _audio = null;
        if (_ssh is { } ssh)
        {
            RemoveRemoteSocket();
            if (!ssh.HasExited)
            {
                try
                {
                    ssh.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                }
            }
        }

        if (_controlPath is { } control)
        {
            try
            {
                File.Delete(control);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
            }
        }

        _acceptor?.Dispose();
        if (!OperatingSystem.IsWindows() && _forwardTarget is { } forward)
        {
            try
            {
                File.Delete(forward);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
            }
        }

        Status = SessionStatus.Disconnected;
    }
}
