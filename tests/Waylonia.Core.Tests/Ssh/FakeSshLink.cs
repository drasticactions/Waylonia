using Waylonia.Sessions;

namespace Waylonia.Tests.Ssh;

internal sealed class FakeSshLink(string destination, ISshPrompter prompter) : ISshLink
{
    private readonly CancellationTokenSource _lost = new();
    private bool _connected;

    public string Destination => destination;

    public ISshPrompter Prompter => prompter;

    public bool IsConnected => _connected && !_lost.IsCancellationRequested;

    public CancellationToken Lost => _lost.Token;

    public Exception? ConnectOutcome { get; set; }

    public Func<FakeSshLink, Task>? BeforeConnect { get; set; }

    public List<string> Events { get; } = [];

    public List<FakeSshCommand> Commands { get; } = [];

    public FakeSshListener? Listener { get; private set; }

    public bool Disposed { get; private set; }

    public IReadOnlyList<string> Scripts => Commands.Select(static command => command.Script).ToList();

    public FakeSshCommand Command(int index) => Commands[index];

    public async Task ConnectAsync(CancellationToken cancellation)
    {
        Events.Add("connect");
        if (BeforeConnect is { } before)
        {
            await before(this);
        }

        if (ConnectOutcome is { } failure)
        {
            throw failure;
        }

        _connected = true;
    }

    public Task<ISshListener> ListenUnixAsync(string remotePath, CancellationToken cancellation)
    {
        Events.Add($"listen {remotePath}");
        Listener = new FakeSshListener(remotePath);
        return Task.FromResult<ISshListener>(Listener);
    }

    public Task<ISshCommand> RunAsync(string script, CancellationToken cancellation)
    {
        if (Disposed || _lost.IsCancellationRequested)
        {
            throw new SshLinkException(SshLinkReason.Lost, SshLinkException.Sentence(SshLinkReason.Lost, destination));
        }

        Events.Add("run");
        var command = new FakeSshCommand(script);
        Commands.Add(command);
        return Task.FromResult<ISshCommand>(command);
    }

    public void Drop()
    {
        _lost.Cancel();
        Listener?.Stop();
        foreach (var command in Commands)
        {
            command.Exit(-1);
        }
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        Events.Add("dispose");
        Drop();
        return ValueTask.CompletedTask;
    }
}
