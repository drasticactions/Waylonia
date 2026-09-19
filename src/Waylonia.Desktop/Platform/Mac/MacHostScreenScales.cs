using Basin.Hosted;

namespace Waylonia;

internal sealed class MacHostScreenScales : IHostScreenScales
{
    public double? TryGetScale(HostScreenInfo info) =>
        OperatingSystem.IsMacOS() ? MacScreenScales.TryGetScale(info) : null;
}
