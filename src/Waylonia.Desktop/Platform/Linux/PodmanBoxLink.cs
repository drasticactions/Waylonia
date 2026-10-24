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
            throw Unreachable($"The session names {box.Kind.Noun}, and the {box.Program} command is not installed.");
        }

        (int Code, string Output, string Error) listed;
        try
        {
            listed = await CliProcess.RunAsync(box.Program, box.ListArguments, cancellation, Environment()).ConfigureAwait(false);
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException)
        {
            throw Unreachable($"The {box.Program} command did not start.", error);
        }

        if (listed.Code != 0)
        {
            throw Unreachable($"The command {box.Program} {string.Join(' ', box.ListArguments)} exited with {listed.Code}.{Said(listed.Error)}");
        }

        if (!box.ParseList(listed.Output).Contains(name, StringComparer.Ordinal))
        {
            throw Unreachable($"There is no {box.Program} container named {name}. Run {box.Program} list to see the containers.");
        }

        Log.Debug($"{destination}: connected, {box.Program} lists the container");
        _connected = true;
    }

    public Task<ISshListener> ListenAsync(string remotePath, CancellationToken cancellation) =>
        throw new NotSupportedException($"The container {destination} takes clients on a display socket, not waypipe channels.");

    public Task<ISessionDisplay> OpenDisplayAsync(DisplayRequest request, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (System.Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is not { Length: > 0 } runtime || !Directory.Exists(runtime))
        {
            throw new SshLinkException(
                SshLinkReason.ForwardRefused,
                $"The container {destination} shares the XDG_RUNTIME_DIR of this machine, and Waylonia has none.");
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
                $"The display socket for {destination} could not be bound in {runtime}. {error.Message}",
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

    private SshLinkException Unreachable(string detail, Exception? inner = null) =>
        new(SshLinkReason.Unreachable, $"Couldn't reach {destination}. {detail}", inner);

    private string Said(string error) =>
        CliProcess.LastLine(error) is { Length: > 0 } line ? $" {box.Program} said: {line}" : string.Empty;

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
