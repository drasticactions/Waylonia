using Basin.Hosted;

namespace Waylonia;

internal sealed class NullHostScreenScales : IHostScreenScales
{
    public static readonly NullHostScreenScales Instance = new();

    public double? TryGetScale(HostScreenInfo info) => null;
}
