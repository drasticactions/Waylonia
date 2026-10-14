using Waylonia.Sessions;

namespace Waylonia.Tests.Ssh;

internal sealed class FakeSshLinkFactory : ITargetLinkFactory
{
    public List<FakeSshLink> Links { get; } = [];

    public Queue<Exception?> ConnectOutcomes { get; } = new();

    public Func<FakeSshLink, Task>? BeforeConnect { get; set; }

    public FakeSshLink? Last => Links.Count > 0 ? Links[^1] : null;

    public ISshPrompter? LastPrompter { get; private set; }

    public IReadOnlyList<string> Listed { get; set; } = [];

    public bool IsAvailable { get; set; } = true;

    public ChannelShape Shape { get; set; } = ChannelShape.Waypipe;

    public string? DisplayDirectory { get; set; }

    public string? LastUser { get; private set; }

    public Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellation) => Task.FromResult(Listed);

    public ISshLink Create(string destination, string? user, ISshPrompter prompter)
    {
        LastUser = user;
        return Create(destination, prompter);
    }

    public ISshLink Create(string destination, ISshPrompter prompter)
    {
        LastPrompter = prompter;
        var link = new FakeSshLink(destination, prompter)
        {
            ConnectOutcome = ConnectOutcomes.Count > 0 ? ConnectOutcomes.Dequeue() : null,
            BeforeConnect = BeforeConnect,
            Shape = Shape,
            DisplayDirectory = DisplayDirectory,
        };
        Links.Add(link);
        return link;
    }
}
