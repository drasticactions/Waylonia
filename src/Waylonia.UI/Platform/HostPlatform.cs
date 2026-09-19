using Waylonia.Audio;
using Waylonia.Sessions;

namespace Waylonia;

internal sealed record HostPlatform(
    HostCapabilities Capabilities,
    WayloniaPaths Paths,
    ISshLinkFactory Links,
    Func<AudioFill, int, int, IAudioSink?> OpenAudio,
    IHostCursor Cursor,
    IHostScreenScales ScreenScales,
    IGlobalHotkeys Hotkeys,
    IHostCapture Capture,
    IHostWindowing Windowing,
    IXWaylandHost XWayland)
{
    public static HostPlatform Minimal(WayloniaPaths paths, ISshLinkFactory links)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(links);
        return new(
            HostCapabilities.None,
            paths,
            links,
            static (_, _, _) => null,
            NullHostCursor.Instance,
            NullHostScreenScales.Instance,
            NullGlobalHotkeys.Instance,
            NullHostCapture.Instance,
            NullHostWindowing.Instance,
            NullXWaylandHost.Instance);
    }
}
