using Basin.Hosted;

namespace Waylonia;

internal interface IHostScreenScales
{
    double? TryGetScale(HostScreenInfo info);
}
