using Waylonia.Audio;
using Waylonia.Sessions;

namespace Waylonia;

internal static partial class DesktopHead
{
    private static partial HostCapabilities PlatformCapabilities() => new(
        Tray: true,
        GlobalHotkeys: true,
        KeyboardCapture: true,
        LocalCommands: false,
        LocalDesktops: false,
        XWayland: false,
        LocalApplications: false,
        ChannelsOnly: true);

    private static partial WayloniaPaths PlatformPaths() => LocalApplicationDataPaths();

    private static partial HostPlatform Compose(
        HostCapabilities capabilities,
        WayloniaPaths paths,
        ISshLinkFactory links,
        Func<AudioFill, int, int, IAudioSink?> audio) => new(
            capabilities,
            paths,
            links,
            audio,
            new WindowsHostCursor(),
            NullHostScreenScales.Instance,
            new WindowsGlobalHotkeys(),
            new WindowsHostCapture(),
            new WindowsHostWindowing(),
            NullXWaylandHost.Instance);
}
