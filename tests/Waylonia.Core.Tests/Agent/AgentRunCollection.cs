using Xunit;

namespace Waylonia.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AgentRunCollection
{
    public const string Name = "Agent runs";
}
