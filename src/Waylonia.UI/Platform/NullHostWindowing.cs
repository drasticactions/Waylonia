using Avalonia;

namespace Waylonia;

internal sealed class NullHostWindowing : IHostWindowing
{
    public static readonly NullHostWindowing Instance = new();

    public AppBuilder Configure(AppBuilder builder) => builder;
}
