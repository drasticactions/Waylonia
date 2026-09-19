using Xunit;

namespace Waylonia.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LogCaptureCollection
{
    public const string Name = "log capture";
}
