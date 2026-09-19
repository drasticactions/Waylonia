using System.Text;
using Basin.Freedesktop;
using Waylonia.Audio;
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

    private static readonly TimeSpan SocketRemovalLimit = TimeSpan.FromSeconds(2);

    private static int _counter;

    private readonly ISessionHost _host;
    private readonly string _remoteSocket;
    private readonly string _displayName;
    private readonly string _xDisplayFile;
    private readonly string? _configDir;
    private readonly string _sinkName;
    private readonly string _tag;
    private readonly List<ISshCommand> _launched = [];
    private readonly object _gate = new();
    private readonly OutputTail _output = new();
    private ISshLink? _link;
    private ISshCommand? _master;
    private ISshListener? _listener;
    private TaskCompletionSource? _scriptStarted;
    private WaypipeAcceptor? _acceptor;
    private WayloniaAudio? _audio;
    private Task<bool>? _connecting;
    private bool _disconnecting;
    private ISshCommand? _commandClient;
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

    public bool CommandFinished { get; private set; }

    public string RemoteDisplay => _displayName;

    public string RemoteSocket => _remoteSocket;

    internal TimeSpan ScriptStartLimit { get; set; } = TimeSpan.FromSeconds(20);

    public IReadOnlyList<ApplicationMenuItem>? Applications { get; private set; }

    private readonly Dictionary<string, string?> _icons = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DesktopEntry> _entriesByAppId = new(StringComparer.Ordinal);
    private readonly Lock _iconLock = new();
    private Task _iconFetch = Task.CompletedTask;

    public event Action<SshSession>? IconsChanged;

    public string? ApplicationsNotice { get; private set; }

    public WaypipeAcceptor? Acceptor => _acceptor;

    public event Action<SshSession>? Changed;

    public event Action<SshSession, int>? Ended;

    public IReadOnlyList<string> RecentOutput => _output.Lines();

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

            if (Status == SessionStatus.Connected && _link is { IsConnected: true })
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
        _commandClient = null;
        CommandFinished = false;
        LastError = null;
        _output.Clear();
        SetStatus(SessionStatus.Connecting, $"logging in to {Ssh}");
        var link = _host.Links.Create(Ssh, _host.Prompter);
        _link = link;
        try
        {
            await link.ConnectAsync(CancellationToken.None);
        }
        catch (SshLinkException error)
        {
            return await FailAsync(link, error.Message, report: error.Reason != SshLinkReason.Cancelled);
        }

        if (_disconnecting || _host.ShuttingDown)
        {
            return await FailAsync(link, "the login was interrupted", report: false);
        }

        var acceptor = EnsureAcceptor();
        ISshListener listener;
        try
        {
            listener = await link.ListenUnixAsync(_remoteSocket, CancellationToken.None);
        }
        catch (SshLinkException error)
        {
            return await FailAsync(link, error.Message, report: true);
        }

        _listener = listener;
        _ = AcceptLoopAsync(link, listener, acceptor);

        ISshCommand master;
        try
        {
            master = await link.RunAsync(RemoteScript(), CancellationToken.None);
        }
        catch (SshLinkException error)
        {
            listener.Dispose();
            return await FailAsync(link, error.Message, report: true);
        }

        _master = master;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _scriptStarted = started;
        _ = DrainAsync(master, _output, line => line == LoggedIn && started.TrySetResult());
        if (!await WaitForScriptAsync(master, started))
        {
            var exited = master.Exited.IsCompleted;
            var message = exited
                ? $"the session script on {Ssh} exited with {master.Exited.Result}" + Detail()
                : started.Task.IsCompleted
                    ? "the login was interrupted"
                    : $"the session script on {Ssh} did not start within {ScriptStartLimit.TotalSeconds:0} seconds" + Detail();
            listener.Dispose();
            master.Dispose();
            return await FailAsync(link, message, report: !_disconnecting && !_host.ShuttingDown);
        }

        if (Settings.Audio && _audio is null)
        {
            _audio = WayloniaAudio.TryStart(_host.Audio, Ssh, link, _sinkName, Settings.AudioFormat);
        }

        SetStatus(SessionStatus.Connected, $"connected to {Ssh}");
        _ = WatchLinkAsync(link, master);
        if (Settings.Desktop is { } recipe)
        {
            var environment = DesktopSession.Environment(recipe, Settings.DesktopEnv, Settings.Gpu);
            var wrapper = DesktopSession.Wrapper(recipe, _displayName, recipe.Command, environment);
            _commandClient = await StartRemoteClientAsync(wrapper, exportDisplay: false);
            SetStatus(SessionStatus.Connected, $"starting {recipe.Name} on {Ssh}");
            _ = WatchArrivalAsync(link, master, recipe.Name);
        }
        else
        {
            if (Settings.Command is { } command)
            {
                _commandClient = await StartRemoteClientAsync(command);
                _ = WatchArrivalAsync(link, master, command);
            }
            else if (Settings.Autostart.Count == 0)
            {
                Log.Info($"{Ssh} is open, waiting for client");
                SetStatus(SessionStatus.Connected, $"connected to {Ssh}, waiting for a client");
            }

            foreach (var autostart in Settings.Autostart)
            {
                await StartRemoteClientAsync(autostart);
            }

            _ = LoadApplicationsAsync();
        }

        return true;
    }

    private async Task<bool> FailAsync(ISshLink link, string message, bool report)
    {
        LastError = message;
        if (report)
        {
            Log.Error($"{message}");
            Report(_output);
        }

        if (ReferenceEquals(link, _link))
        {
            _link = null;
            _master = null;
            _listener = null;
            _scriptStarted = null;
        }

        await link.DisposeAsync();
        SetStatus(SessionStatus.Disconnected, message);
        if (!_disconnecting && !_host.ShuttingDown)
        {
            Ended?.Invoke(this, 1);
        }

        return false;
    }

    private string Detail() => _output.LastLine() is { } line ? $": {line}" : string.Empty;

    private WaypipeAcceptor EnsureAcceptor()
    {
        if (_acceptor is { } existing)
        {
            return existing;
        }

        var acceptor = new WaypipeAcceptor(
            Name, _host, Settings.Compression, Settings.Gpu, Settings.Video, Settings.VideoDecoder, Settings);
        acceptor.Changed += () => Changed?.Invoke(this);
        acceptor.Failed += error =>
        {
            LastError = $"the channel listener failed: {error.Message}";
            SetStatus(Status, LastError);
        };
        _acceptor = acceptor;
        return acceptor;
    }

    private async Task AcceptLoopAsync(ISshLink link, ISshListener listener, WaypipeAcceptor acceptor)
    {
        try
        {
            while (await listener.AcceptAsync(CancellationToken.None) is { } stream)
            {
                if (!ReferenceEquals(link, _link) || _disposed)
                {
                    await stream.DisposeAsync();
                    return;
                }

                acceptor.Adopt(stream);
            }
        }
        catch (Exception error) when (error is IOException or ObjectDisposedException or InvalidOperationException)
        {
            if (ReferenceEquals(link, _link) && !_disconnecting)
            {
                acceptor.Fail(error);
            }
        }
    }

    private static async Task DrainAsync(ISshCommand command, OutputTail tail, Func<string, bool>? claim = null)
    {
        var errors = Task.Run(async () =>
        {
            try
            {
                await foreach (var line in command.ErrorLines(CancellationToken.None))
                {
                    tail.Add(line);
                }
            }
            catch (Exception error) when (error is OperationCanceledException or ObjectDisposedException)
            {
            }
        });
        try
        {
            using var reader = new StreamReader(command.Output, new UTF8Encoding(false, false), false, 4096, leaveOpen: true);
            while (await reader.ReadLineAsync() is { } line)
            {
                if (claim is not null && claim(line))
                {
                    continue;
                }

                tail.Add(line);
            }
        }
        catch (Exception error) when (error is IOException or ObjectDisposedException or OperationCanceledException or InvalidOperationException)
        {
        }

        await errors;
    }

    private static void Report(OutputTail tail)
    {
        foreach (var line in tail.Lines())
        {
            Log.Error($"ssh: {line}");
        }
    }

    private async Task<bool> WaitForScriptAsync(ISshCommand master, TaskCompletionSource started)
    {
        await Task.WhenAny(started.Task, master.Exited, Task.Delay(ScriptStartLimit));
        return started.Task.IsCompleted && !master.Exited.IsCompleted && !_host.ShuttingDown && !_disconnecting;
    }

    private static Task WhenLost(ISshLink link)
    {
        var lost = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        link.Lost.Register(() => lost.TrySetResult());
        return lost.Task;
    }

    private async Task WatchArrivalAsync(ISshLink link, ISshCommand master, string what)
    {
        var acceptor = _acceptor!;
        var attachedTask = Task.Run(async () =>
        {
            while (acceptor.Attached == 0 && !_host.ShuttingDown && !_disconnecting)
            {
                await Task.Delay(200);
            }
        });
        await Task.WhenAny(attachedTask, master.Exited, WhenLost(link), Task.Delay(TimeSpan.FromSeconds(30)));
        if (acceptor.Attached > 0 || _disconnecting || _host.ShuttingDown || !ReferenceEquals(link, _link))
        {
            return;
        }

        if (master.Exited.IsCompleted || link.Lost.IsCancellationRequested)
        {
            return;
        }

        LastError = $"no channel arrived from {Ssh} within 30 seconds of starting '{what}'";
        Log.Error($"{LastError}");
        Report(_output);
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

        if (await StartRemoteClientAsync(command) is null)
        {
            Log.Warn($"{label}: '{command}' failed to start on {Ssh}");
            return false;
        }

        _host.Status($"started '{command}' on {Ssh}");
        return true;
    }

    private async Task<bool> EnsureConnectedAsync()
    {
        Task<bool>? pending;
        lock (_gate)
        {
            pending = _connecting is { IsCompleted: false } connecting ? connecting : null;
        }

        if (pending is not null)
        {
            return await pending;
        }

        if (_link is { IsConnected: true } && Status == SessionStatus.Connected)
        {
            return true;
        }

        Log.Info($"the connection to {Ssh} is gone; opening it again");
        _host.Status($"reconnecting to {Ssh}");
        return await ConnectAsync();
    }

    private ISshLink? LiveLink() => _link is { IsConnected: true } link && Status == SessionStatus.Connected ? link : null;

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
            if (LiveLink() is null)
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
        catch (Exception error) when (error is IOException or InvalidOperationException)
        {
            Log.Warn($"{Name}: the application list could not be read: {error.Message}");
            Notice("The applications could not be read");
            return;
        }

        lock (_iconLock)
        {
            _entriesByAppId.Clear();
            foreach (var entry in listable)
            {
                var id = entry.Id.EndsWith(".desktop", StringComparison.Ordinal) ? entry.Id[..^".desktop".Length] : entry.Id;
                _entriesByAppId.TryAdd(id, entry);
                if (entry.StartupWMClass is { Length: > 0 } wmClass)
                {
                    _entriesByAppId.TryAdd(wmClass, entry);
                }
            }
        }

        var items = ApplicationMenu.Build(listable, terminal, EntryIcon, _host.ThemeIconFor);
        if (terminal is null && listable.Any(static entry => entry.Terminal))
        {
            Log.Info($"{Name}: set terminal in the config to list terminal applications");
        }

        Log.Debug($"{Name}: {listable.Count} application(s) in {items.Count} categor(ies) for the tray menu");
        Applications = items;
        ApplicationsNotice = items.Count == 0 ? "No applications found" : null;
        Changed?.Invoke(this);
        var wanted = listable.Select(static entry => entry.Icon).Where(static icon => icon is { Length: > 0 }).Select(static icon => icon!).ToList();
        if (wanted.Count > 0 && _host.Icons is not null)
        {
            _ = FetchIconsAsync(wanted).ContinueWith(
                _ =>
                {
                    Applications = ApplicationMenu.Build(listable, terminal, EntryIcon, _host.ThemeIconFor);
                    Changed?.Invoke(this);
                },
                TaskScheduler.Default);
        }
    }

    private string? EntryIcon(DesktopEntry entry) => IconPathFor(entry.Icon) ?? _host.ThemeIconFor(entry);

    public string? IconPathFor(string? name)
    {
        if (name is not { Length: > 0 } || _host.Icons is not { } cache)
        {
            return null;
        }

        lock (_iconLock)
        {
            if (_icons.TryGetValue(name, out var known))
            {
                return known;
            }
        }

        var cached = cache.Find(Name, name);
        if (cached is not null)
        {
            lock (_iconLock)
            {
                _icons[name] = cached;
            }
        }

        return cached;
    }

    public string? IconNameForAppId(string appId)
    {
        lock (_iconLock)
        {
            return _entriesByAppId.GetValueOrDefault(appId)?.Icon;
        }
    }

    public Task FetchIconsAsync(IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        var pending = new List<string>();
        lock (_iconLock)
        {
            foreach (var name in names.Distinct(StringComparer.Ordinal))
            {
                if (RemoteIcons.IsSafeName(name) && !_icons.ContainsKey(name) && _host.Icons?.Find(Name, name) is null)
                {
                    pending.Add(name);
                }
            }

            if (pending.Count == 0)
            {
                return _iconFetch;
            }

            _iconFetch = _iconFetch.ContinueWith(_ => FetchIconsCoreAsync(pending), TaskScheduler.Default).Unwrap();
            return _iconFetch;
        }
    }

    private async Task FetchIconsCoreAsync(IReadOnlyList<string> names)
    {
        if (_host.Icons is not { } cache || LiveLink() is null)
        {
            return;
        }

        string? output;
        try
        {
            output = await ReadRemoteAsync("icons", RemoteIcons.Script(names), TimeSpan.FromSeconds(120));
        }
        catch (Exception error) when (error is IOException or InvalidOperationException)
        {
            Log.Warn($"{Name}: the icons could not be fetched: {error.Message}");
            return;
        }

        if (output is null)
        {
            return;
        }

        var fetched = RemoteIcons.Parse(output);
        lock (_iconLock)
        {
            foreach (var icon in fetched.Icons)
            {
                _icons[icon.Name] = cache.Store(Name, icon, Log);
            }

            foreach (var missing in fetched.Missing)
            {
                _icons.TryAdd(missing, null);
            }
        }

        Log.Debug($"{Name}: fetched {fetched.Icons.Count} icon(s), {fetched.Missing.Count} missing");
        IconsChanged?.Invoke(this);
    }

    private void Notice(string text)
    {
        Applications = null;
        ApplicationsNotice = text;
        Changed?.Invoke(this);
    }

    private Task<string?> ReadRemoteApplicationsAsync(DesktopLocale locale) =>
        ReadRemoteAsync("applications", RemoteApplications.Script(locale), TimeSpan.FromSeconds(90));

    private async Task<string?> ReadRemoteAsync(string what, string script, TimeSpan limit)
    {
        if (LiveLink() is not { } link)
        {
            return null;
        }

        ISshCommand command;
        try
        {
            command = await link.RunAsync(script, CancellationToken.None);
        }
        catch (SshLinkException error)
        {
            Log.Warn($"reading the {what} on {Ssh} failed: {error.Message}");
            return null;
        }

        using (command)
        {
            var errors = new OutputTail();
            var drain = Task.Run(async () =>
            {
                await foreach (var line in command.ErrorLines(CancellationToken.None))
                {
                    errors.Add(line);
                    _output.Add(line);
                }
            });
            using var timeout = new CancellationTokenSource(limit);
            try
            {
                using var reader = new StreamReader(command.Output, new UTF8Encoding(false, false), false, 4096, leaveOpen: true);
                var text = await reader.ReadToEndAsync(timeout.Token);
                var code = await command.Exited.WaitAsync(timeout.Token);
                if (code != 0 && !_disconnecting && !_host.ShuttingDown)
                {
                    Log.Warn($"reading the {what} on {Ssh} exited with {code}");
                    await drain;
                    Report(errors);
                }

                return text;
            }
            catch (OperationCanceledException)
            {
                Log.Warn($"reading the {what} on {Ssh} took over {limit.TotalSeconds:0} s; giving up");
                return null;
            }
        }
    }

    private string RemoteLang =>
        Settings.Lang is { } lang ? $"if [ -z \"$LANG\" ]; then LANG={lang}; export LANG; fi; " : string.Empty;

    internal string ClientScript(string command, bool exportDisplay = true)
    {
        var quoted = command.Replace("'", "'\\''", StringComparison.Ordinal);
        var pulse = Settings.Audio
            ? $"PULSE_SINK={_sinkName} PIPEWIRE_NODE={_sinkName} "
            : string.Empty;
        var gtkConfig = _configDir is { } configDir
            ? $"if [ -d {configDir} ]; then XDG_CONFIG_HOME={configDir}; export XDG_CONFIG_HOME; fi; "
            : string.Empty;
        var display = exportDisplay
            ? $"if [ -s {_xDisplayFile} ]; then DISPLAY=$(cat {_xDisplayFile}); export DISPLAY; fi; "
            : string.Empty;
        return
            RemoteRuntimeDir +
            RemoteLang +
            $"d=\"$XDG_RUNTIME_DIR/{_displayName}\"; i=0; " +
            $"while [ ! -S \"$d\" ] && [ $i -lt 50 ]; do sleep 0.2; i=$((i+1)); done; " +
            display +
            gtkConfig +
            $"{pulse}XDG_SESSION_TYPE=wayland WAYLAND_DISPLAY={_displayName} sh -c '{quoted}'";
    }

    private async Task<ISshCommand?> StartRemoteClientAsync(string command, bool exportDisplay = true)
    {
        if (LiveLink() is not { } link)
        {
            Log.Warn($"'{command}' cannot start on {Ssh}: the remote session is not up");
            return null;
        }

        ISshCommand started;
        try
        {
            started = await link.RunAsync(ClientScript(command, exportDisplay), CancellationToken.None);
        }
        catch (SshLinkException error)
        {
            Log.Warn($"'{command}' failed to start on {Ssh}: {error.Message}");
            return null;
        }

        lock (_launched)
        {
            _launched.Add(started);
        }

        _ = WatchClientAsync(started, command);
        return started;
    }

    private async Task WatchClientAsync(ISshCommand client, string command)
    {
        var tail = new OutputTail();
        var drained = DrainAsync(client, tail);
        var code = await client.Exited;
        await drained;
        bool last;
        lock (_launched)
        {
            _launched.Remove(client);
            last = _launched.Count == 0;
        }

        client.Dispose();
        if (_host.ShuttingDown || _disconnecting)
        {
            return;
        }

        if (code != 0)
        {
            Log.Warn($"'{command}' exited with {code}");
            foreach (var line in tail.Lines())
            {
                Log.Error($"{command}: {line}");
            }
        }

        if (Settings.AdHoc && last && ReferenceEquals(client, _commandClient))
        {
            Log.Info($"'{command}' on {Ssh} has ended and nothing else was started there; disconnecting");
            CommandFinished = true;
            await DisconnectAsync();
            Ended?.Invoke(this, code == 0 ? 0 : 1);
        }
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

    internal string RemoveSocketScript()
    {
        var removeConfig = _configDir is { } configDir ? $"; rm -rf {configDir}" : string.Empty;
        return RemoteRuntimeDir + $"rm -f {_remoteSocket} {_xDisplayFile} \"$XDG_RUNTIME_DIR/{_displayName}\"{removeConfig}";
    }

    private async Task WatchLinkAsync(ISshLink link, ISshCommand master)
    {
        await Task.WhenAny(WhenLost(link), master.Exited);
        if (_host.ShuttingDown || _disconnecting || !ReferenceEquals(link, _link))
        {
            return;
        }

        var hadClients = HadClients;
        var code = master.Exited.IsCompleted ? master.Exited.Result : -1;
        var clean = code == 0;
        LastError = code > 0
            ? $"the session script on {Ssh} exited with {code}" + Detail()
            : $"the connection to {Ssh} ended";
        if (hadClients || clean)
        {
            Log.Info($"the connection to {Ssh} ended");
        }
        else
        {
            Log.Error($"{LastError}");
            Report(_output);
        }

        StopClients();
        _listener?.Dispose();
        master.Dispose();
        await link.DisposeAsync();
        ReleaseLocal();
        _link = null;
        _master = null;
        _listener = null;
        _scriptStarted = null;
        SetStatus(SessionStatus.Disconnected, $"disconnected from {Ssh}");
        Ended?.Invoke(this, clean ? 0 : 1);
    }

    private void ReleaseLocal()
    {
        _audio?.Dispose();
        _audio = null;
        _acceptor?.CloseChannels();
    }

    public bool StopClients()
    {
        ISshCommand[] launched;
        lock (_launched)
        {
            launched = [.. _launched];
            _launched.Clear();
        }

        foreach (var client in launched)
        {
            client.TrySignal("TERM");
            client.Dispose();
        }

        return launched.Length > 0;
    }

    public async Task DisconnectAsync()
    {
        if (_link is null && Status == SessionStatus.Disconnected)
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
        if (_link is { } link)
        {
            await RemoveRemoteSocketAsync(link);
            _listener?.Dispose();
            _master?.Dispose();
            await link.DisposeAsync();
        }

        ReleaseLocal();
        _link = null;
        _master = null;
        _listener = null;
        _scriptStarted = null;
        Applications = null;
        ApplicationsNotice = null;
        SetStatus(SessionStatus.Disconnected, $"disconnected from {Ssh}");
    }

    private async Task RemoveRemoteSocketAsync(ISshLink link)
    {
        if (!link.IsConnected)
        {
            return;
        }

        try
        {
            using var remove = await link.RunAsync(RemoveSocketScript(), CancellationToken.None).WaitAsync(SocketRemovalLimit);
            var complaints = new OutputTail();
            var drain = DrainAsync(remove, complaints);
            var code = await remove.Exited.WaitAsync(SocketRemovalLimit);
            if (code != 0)
            {
                await drain.WaitAsync(SocketRemovalLimit);
                Log.Debug($"{_remoteSocket} may still exist on {Ssh}: {complaints.LastLine()}");
            }
        }
        catch (Exception error) when (error is SshLinkException or TimeoutException or IOException)
        {
            Log.Debug($"{_remoteSocket} could not be removed from {Ssh}: {error.Message}");
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
        if (_link is { } link)
        {
            try
            {
                RemoveRemoteSocketAsync(link).Wait(SocketRemovalLimit + SocketRemovalLimit);
            }
            catch (AggregateException)
            {
            }

            _listener?.Dispose();
            _master?.Dispose();
            link.DisposeAsync().AsTask().Wait();
            _link = null;
            _master = null;
            _listener = null;
        }

        _acceptor?.Dispose();
        Status = SessionStatus.Disconnected;
    }
}
