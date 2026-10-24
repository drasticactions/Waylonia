using System.ComponentModel;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using static Waylonia.WayloniaLog;

namespace Waylonia.Sessions;

[System.Runtime.Versioning.SupportedOSPlatform("linux")]
internal sealed class IncusLink(string destination, string name, string? userName) : ISshLink
{
    private static readonly TimeSpan ContainerStartLimit = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan VmStartLimit = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan DeviceLimit = TimeSpan.FromSeconds(15);

    private readonly CancellationTokenSource _lost = new();
    private readonly List<CliCommand> _commands = [];
    private IncusInstance? _instance;
    private IncusUser _user = IncusUser.Root;
    private bool _connected;
    private bool _disposed;

    public string Destination => destination;

    public bool IsConnected => _connected && !_disposed && !_lost.IsCancellationRequested;

    public CancellationToken Lost => _lost.Token;

    public ChannelShape Shape => _instance is { IsVm: true } ? ChannelShape.Waypipe : ChannelShape.Direct;

    public string? Notice { get; private set; }

    public static string StateDirectory()
    {
        var state = Environment.GetEnvironmentVariable("XDG_STATE_HOME") is { Length: > 0 } configured && Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "state");
        return Path.Combine(state, "waylonia", "incus");
    }

    public async Task ConnectAsync(CancellationToken cancellation)
    {
        if (!CliProcess.OnPath(Incus.Program))
        {
            throw Unreachable($"The session names {DestinationKinds.Incus.Noun}, and the incus command is not installed.");
        }

        var instance = await ReadAsync(cancellation).ConfigureAwait(false);
        if (!instance.IsRunning)
        {
            Log.Info($"{destination}: starting the stopped instance");
            var started = await IncusAsync(Incus.Start(name), cancellation).ConfigureAwait(false);
            if (started.Code != 0)
            {
                throw Unreachable($"The command incus start {name} failed.{Said(started.Error)}");
            }
        }

        await WaitForExecAsync(instance.IsVm ? VmStartLimit : ContainerStartLimit, cancellation).ConfigureAwait(false);
        _instance = instance.IsRunning && (!instance.IsVm || instance.VsockId is not null) ? instance : await ReadAsync(cancellation).ConfigureAwait(false);
        _user = await ResolveUserAsync(cancellation).ConfigureAwait(false);
        Log.Debug($"{destination}: connected, {(_instance.IsVm ? "VM" : "container")} in project {_instance.Project}, scripts run as {_user.Name}");
        _connected = true;
    }

    private async Task<IncusInstance> ReadAsync(CancellationToken cancellation)
    {
        var listed = await IncusAsync(Incus.List(), cancellation).ConfigureAwait(false);
        if (listed.Code != 0)
        {
            throw Unreachable($"The command incus list failed.{Said(listed.Error)}");
        }

        IReadOnlyList<IncusInstance> instances;
        try
        {
            instances = Incus.ParseInstances(listed.Output);
        }
        catch (System.Text.Json.JsonException error)
        {
            throw Unreachable("The command incus list printed something other than JSON.", error);
        }

        return instances.FirstOrDefault(instance => instance.Name == name)
            ?? throw Unreachable($"There is no Incus instance named {name}. Run incus list to see the instances.");
    }

    private async Task WaitForExecAsync(TimeSpan limit, CancellationToken cancellation)
    {
        var deadline = DateTime.UtcNow + limit;
        var last = string.Empty;
        while (true)
        {
            var probe = await IncusAsync(Incus.Probe(name), cancellation).ConfigureAwait(false);
            if (probe.Code == 0)
            {
                return;
            }

            last = CliProcess.LastLine(probe.Error);
            if (DateTime.UtcNow >= deadline)
            {
                throw Unreachable($"The instance {name} did not accept a command within {limit.TotalSeconds:0} seconds.{Said(last)}");
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellation).ConfigureAwait(false);
        }
    }

    private async Task<IncusUser> ResolveUserAsync(CancellationToken cancellation)
    {
        var key = userName ?? "1000";
        var found = await IncusAsync(Incus.Passwd(name, key), cancellation).ConfigureAwait(false);
        if (found.Code == 0 && Incus.ParsePasswd(found.Output) is { } user)
        {
            return user;
        }

        if (userName is not null)
        {
            throw new SshLinkException(SshLinkReason.AuthenticationFailed, $"The instance {name} has no user named {userName}.");
        }

        Notice = "The instance has no account with uid 1000, so scripts run as root.";
        Log.Info($"{destination}: no account with uid 1000, scripts run as root");
        return IncusUser.Root;
    }

    public Task<ISshListener> ListenAsync(string remotePath, CancellationToken cancellation)
    {
        if (_instance is not { IsVm: true, VsockId: { } cid })
        {
            throw new SshLinkException(SshLinkReason.ForwardRefused, $"The instance {name} has no vsock id, so it is not a running Incus VM.");
        }

        if (!File.Exists(IncusVsockListener.VhostDevice))
        {
            throw new SshLinkException(
                SshLinkReason.ForwardRefused, "This machine has no vhost-vsock device. Load the vhost_vsock module to reach Incus VMs.");
        }

        try
        {
            return Task.FromResult<ISshListener>(IncusVsockListener.Bind(cid, destination));
        }
        catch (SocketException error)
        {
            throw new SshLinkException(SshLinkReason.ForwardRefused, $"A vsock listener for {destination} could not be bound. {error.Message}", error);
        }
    }

    public async Task<ISessionDisplay> OpenDisplayAsync(DisplayRequest request, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(request);
        var instance = _instance ?? throw new InvalidOperationException("The link is not connected. Call ConnectAsync first.");
        var root = StateDirectory();
        await SweepAsync(instance, root, cancellation).ConfigureAwait(false);
        var device = request.Name;
        var directory = Path.Combine(root, device);
        try
        {
            Directory.CreateDirectory(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Directory.CreateDirectory(directory);
            File.SetUnixFileMode(
                directory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new SshLinkException(SshLinkReason.ForwardRefused, $"The directory {directory} could not be made. {error.Message}", error);
        }

        var added = await IncusAsync(Incus.AddDisk(name, device, directory, Incus.MountPath(device)), cancellation).ConfigureAwait(false);
        if (added.Code != 0)
        {
            TryDelete(directory);
            throw new SshLinkException(
                SshLinkReason.ForwardRefused,
                $"Incus refused the display device for {name}.{Said(added.Error)}");
        }

        var x11 = await AddX11Async(instance, device, request.XDisplay, cancellation).ConfigureAwait(false);
        if (request.Gpu && !instance.HasGpu)
        {
            Log.Warn($"{destination}: no gpu device, so clients draw in shared memory, 'incus config device add {name} gpu gpu' adds one");
        }

        try
        {
            return UnixSessionDisplay.Bind(
                Path.Combine(directory, "wayland-0"),
                (UnixFileMode)0b111_111_111,
                Incus.ClientDisplay(device),
                x11,
                () => Release(device, directory, x11 is not null));
        }
        catch (Exception error) when (error is SocketException or IOException or UnauthorizedAccessException)
        {
            Release(device, directory, x11 is not null);
            throw new SshLinkException(SshLinkReason.ForwardRefused, $"The display socket for {destination} could not be bound. {error.Message}", error);
        }
    }

    private async Task<string?> AddX11Async(IncusInstance instance, string device, string? xDisplay, CancellationToken cancellation)
    {
        if (Incus.DisplayNumber(xDisplay) is not { } number)
        {
            return null;
        }

        var added = await IncusAsync(
            Incus.AddX11Proxy(name, Incus.X11Device(device), number, (int)getuid(), (int)getgid()), cancellation).ConfigureAwait(false);
        if (added.Code == 0)
        {
            return $":{number}";
        }

        var said = CliProcess.LastLine(added.Error);
        if (said.Contains("Proxy devices are forbidden", StringComparison.OrdinalIgnoreCase))
        {
            Log.Warn($"{destination}: no X11 proxy device, project {instance.Project} forbids proxy devices, so X11 applications get no DISPLAY");
        }
        else
        {
            Log.Warn($"{destination}: no X11 proxy device, so X11 applications get no DISPLAY, incus said {said}");
        }

        return null;
    }

    private async Task SweepAsync(IncusInstance instance, string root, CancellationToken cancellation)
    {
        foreach (var stale in Incus.StaleDevices(instance.Devices, IsLiveWaylonia))
        {
            var removed = await IncusAsync(Incus.RemoveDevice(name, stale), cancellation).ConfigureAwait(false);
            if (removed.Code == 0)
            {
                Log.Info($"{destination}: removed the stale device {stale}, left by a waylonia that is gone");
            }
            else
            {
                Log.Warn($"{destination}: stale device {stale} not removed, incus said {CliProcess.LastLine(removed.Error)}");
            }

            if (!stale.EndsWith("-x11", StringComparison.Ordinal))
            {
                TryDelete(Path.Combine(root, stale));
            }
        }
    }

    private static bool IsLiveWaylonia(int pid)
    {
        if (pid == Environment.ProcessId)
        {
            return true;
        }

        try
        {
            var target = File.ResolveLinkTarget($"/proc/{pid}/exe", returnFinalTarget: true);
            var self = Environment.ProcessPath is { } path ? File.ResolveLinkTarget(path, returnFinalTarget: true)?.FullName ?? path : null;
            return target is not null && target.FullName == self;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return Directory.Exists($"/proc/{pid}");
        }
    }

    private void Release(string device, string directory, bool x11)
    {
        if (x11)
        {
            RemoveDevice(Incus.X11Device(device));
        }

        RemoveDevice(device);
        TryDelete(directory);
    }

    private void RemoveDevice(string device)
    {
        try
        {
            var removing = IncusAsync(Incus.RemoveDevice(name, device), CancellationToken.None);
            if (!removing.Wait(DeviceLimit) || removing.Result.Code != 0)
            {
                Log.Warn($"{destination}: device {device} may still be there, the next connect removes it");
            }
        }
        catch (AggregateException error)
        {
            Log.Warn(error.InnerException ?? error, $"{destination}: device {device} not removed");
        }
    }

    private static void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Log.Debug(error, $"incus: directory {directory} not removed");
        }
    }

    public Task<ISshCommand> RunAsync(string script, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(script);
        if (!IsConnected)
        {
            throw new SshLinkException(SshLinkReason.Lost, SshLinkException.Sentence(SshLinkReason.Lost, destination));
        }

        CliCommand command;
        try
        {
            command = new CliCommand(CliProcess.Start(Incus.Program, Incus.Script(name, _user, script), null, input: true), Signal);
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException)
        {
            throw new SshLinkException(SshLinkReason.Lost, SshLinkException.Sentence(SshLinkReason.Lost, destination), error);
        }

        lock (_commands)
        {
            _commands.Add(command);
        }

        _ = command.Exited.ContinueWith(
            _ =>
            {
                lock (_commands)
                {
                    _commands.Remove(command);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return Task.FromResult<ISshCommand>(command);
    }

    private bool Signal(int pid, string signal)
    {
        if (pid <= 0)
        {
            return false;
        }

        _ = IncusAsync(Incus.Signal(name, signal, pid), CancellationToken.None);
        return true;
    }

    private static async Task<(int Code, string Output, string Error)> IncusAsync(IReadOnlyList<string> arguments, CancellationToken cancellation)
    {
        try
        {
            return await CliProcess.RunAsync(Incus.Program, arguments, cancellation).ConfigureAwait(false);
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException)
        {
            return (-1, string.Empty, error.Message);
        }
    }

    private SshLinkException Unreachable(string detail, Exception? inner = null) =>
        new(SshLinkReason.Unreachable, $"Couldn't reach {destination}. {detail}", inner);

    private static string Said(string error) =>
        CliProcess.LastLine(error) is { Length: > 0 } line ? $" Incus said: {line}" : string.Empty;

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        CliCommand[] open;
        lock (_commands)
        {
            open = [.. _commands];
            _commands.Clear();
        }

        foreach (var command in open)
        {
            command.Dispose();
        }

        _lost.Cancel();
        _lost.Dispose();
        return ValueTask.CompletedTask;
    }

    [DllImport("libc")]
    private static extern uint getuid();

    [DllImport("libc")]
    private static extern uint getgid();
}
