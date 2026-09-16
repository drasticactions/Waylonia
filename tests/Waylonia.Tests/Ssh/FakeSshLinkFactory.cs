using Waylonia.Sessions;

namespace Waylonia.Tests.Ssh;

internal sealed class FakeSshLinkFactory : ISshLinkFactory
{
    public List<FakeSshLink> Links { get; } = [];

    public Queue<Exception?> ConnectOutcomes { get; } = new();

    public Func<FakeSshLink, Task>? BeforeConnect { get; set; }

    public FakeSshLink? Last => Links.Count > 0 ? Links[^1] : null;

    public ISshPrompter? LastPrompter { get; private set; }

    public ISshLink Create(string destination, ISshPrompter prompter)
    {
        LastPrompter = prompter;
        var link = new FakeSshLink(destination, prompter)
        {
            ConnectOutcome = ConnectOutcomes.Count > 0 ? ConnectOutcomes.Dequeue() : null,
            BeforeConnect = BeforeConnect,
        };
        Links.Add(link);
        return link;
    }
}
