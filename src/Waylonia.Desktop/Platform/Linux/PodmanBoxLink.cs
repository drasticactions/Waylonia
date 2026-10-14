using System.ComponentModel;
using static Waylonia.WayloniaLog;

namespace Waylonia.Sessions;

[System.Runtime.Versioning.SupportedOSPlatform("linux")]
internal sealed class PodmanBoxLink(PodmanBox box, string destination, string name) : ISshLink
{
    private readonly CancellationTokenSource _lost = new();
    private readonly List<CliCommand> _commands = [];
    private bool _connected;
    private bool _disposed;

    public string Destination => destination;

    public bool IsConnected => _connected && !_disposed && !_lost.IsCancellationRequested;

    public CancellationToken Lost => _lost.Token;

    public ChannelShape Shape => ChannelShape.Direct;

    public async Task ConnectAsync(CancellationToken cancellation)
    {
        if (!CliProcess.OnPath(box.Program))
        {
            throw Unreachable($"{box.Program} is not installed; the session names {box.Kind.Noun}");
        }

        (int Code, string Output, string Error) listed;
        try
        {
            listed = await CliProcess.RunAsync(box.Program, box.ListArguments, cancellation, Environment()).ConfigureAwait(false);
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException)
        {
            throw Unreachable($"{box.Program} did not start: {error.Message}");
        }

        if (listed.Code != 0)
        {
            throw Unreachable($"'{box.Program} {string.Join(' ', box.ListArguments)}' exited with {listed.Code}: {CliProcess.LastLine(listed.Error)}");
        }

        if (!box.ParseList(listed.Output).Contains(name, StringComparer.Ordinal))
        {
            throw Unreachable($"there is no {box.Program} container named {name}; '{box.Program} list' shows the ones there are");
        }

        Log.Debug($"{destination}: {box.Program} lists {name}");
        _connected = true;
    }

    public Task<ISshListener> ListenAsync(string remotePath, CancellationToken cancellation) =>
        throw new NotSupportedException($"{destination} takes clients on a display socket, not waypipe channels");

    public Task<ISessionDisplay> OpenDisplayAsync(DisplayRequest request, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (System.Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is not { Length: > 0 } runtime || !Directory.Exists(runtime))
        {
            throw new SshLinkException(
                SshLinkReason.ForwardRefused,
                $"{destination} shares this machine's XDG_RUNTIME_DIR, and waylonia has none");
        }

        try
        {
            return Task.FromResult<ISessionDisplay>(UnixSessionDisplay.Bind(
                Path.Combine(runtime, request.Name),
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
                request.Name,
                request.XDisplay));
        }
        catch (Exception error) when (error is System.Net.Sockets.SocketException or IOException or UnauthorizedAccessException)
        {
            throw new SshLinkException(
                SshLinkReason.ForwardRefused,
                $"the display socket for {destination} could not be bound in {runtime}: {error.Message}",
                error);
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
            command = new CliCommand(
                CliProcess.Start(box.Program, box.Run(name, script), Environment(), input: true),
                HostSignals.SignalGroup);
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

    private IReadOnlyDictionary<string, string>? Environment() => box.Environment(CliProcess.HostEnvironment());

    private SshLinkException Unreachable(string detail) =>
        new(SshLinkReason.Unreachable, $"{destination} could not be reached: {detail}");

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
}
