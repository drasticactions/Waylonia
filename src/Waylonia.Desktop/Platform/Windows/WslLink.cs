using System.ComponentModel;
using System.Net.Sockets;
using static Waylonia.WayloniaLog;

namespace Waylonia.Sessions;

internal sealed class WslLink(string destination, string distro) : ISshLink
{
    private readonly CancellationTokenSource _lost = new();
    private readonly List<WslCommand> _commands = [];
    private Guid _vmId;
    private bool _connected;
    private bool _disposed;

    public string Destination => destination;

    public bool IsConnected => _connected && !_disposed && !_lost.IsCancellationRequested;

    public CancellationToken Lost => _lost.Token;

    public async Task ConnectAsync(CancellationToken cancellation)
    {
        (int Code, string Output, string Error) result;
        try
        {
            result = await WslProcess.RunAsync(WslDestination.VmId(distro), cancellation).ConfigureAwait(false);
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException)
        {
            throw Unreachable("The wsl.exe program did not start. WSL is not installed.", error);
        }

        if (result.Code != 0)
        {
            var said = WslProcess.LastLine(result.Error + "\n" + result.Output);
            throw Unreachable(
                said.Contains("wslinfo", StringComparison.OrdinalIgnoreCase)
                    ? "This WSL has no wslinfo --vm-id, which needs WSL 2.6.1 or later. Run wsl --update."
                    : said.Length > 0 ? $"WSL said: {said}" : $"The wsl.exe program exited with {result.Code}.",
                null);
        }

        if (!Guid.TryParse(WslProcess.LastLine(result.Output).Trim('{', '}'), out _vmId))
        {
            throw Unreachable($"The command wslinfo --vm-id printed no VM id. It printed: {WslProcess.LastLine(result.Output)}", null);
        }

        Log.Debug($"{destination}: connected, the WSL VM is {_vmId}");
        _connected = true;
    }

    public Task<ISshListener> ListenAsync(string remotePath, CancellationToken cancellation)
    {
        try
        {
            return Task.FromResult<ISshListener>(WslListener.Bind(_vmId));
        }
        catch (SocketException error)
        {
            throw new SshLinkException(
                SshLinkReason.ForwardRefused,
                $"Windows refused a Hyper-V socket on the WSL VM {_vmId}. {error.Message}",
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

        WslCommand command;
        try
        {
            command = new WslCommand(distro, script);
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

    private SshLinkException Unreachable(string detail, Exception? inner) =>
        new(SshLinkReason.Unreachable, $"Couldn't reach {destination}. {detail}", inner);

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        WslCommand[] open;
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
