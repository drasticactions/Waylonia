using Waylonia.Sessions;

namespace Waylonia;

internal sealed record DesktopPlatform(
    HostPlatform Base,
    IGlobalHotkeys Hotkeys,
    IHostCapture Capture,
    IXWaylandHost XWayland)
{
    public static DesktopPlatform Minimal(WayloniaPaths paths, ISshLinkFactory links) => new(
        HostPlatform.Minimal(paths, links),
        NullGlobalHotkeys.Instance,
        NullHostCapture.Instance,
        NullXWaylandHost.Instance);
}
