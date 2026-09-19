using Waylonia.Audio;
using Waylonia.Sessions;

namespace Waylonia;

internal static partial class DesktopHead
{
    private static partial HostCapabilities PlatformCapabilities() => new(
        Tray: true,
        GlobalHotkeys: true,
        KeyboardCapture: true,
        LocalCommands: true,
        LocalDesktops: true,
        XWayland: true,
        LocalApplications: true,
        ChannelsOnly: false);

    private static partial WayloniaPaths PlatformPaths() => WayloniaPaths.Xdg();

    private static partial HostPlatform Compose(
        HostCapabilities capabilities,
        WayloniaPaths paths,
        ISshLinkFactory links,
        Func<AudioFill, int, int, IAudioSink?> audio) => new(
            capabilities,
            paths,
            links,
            audio,
            new LinuxHostCursor(),
            NullHostScreenScales.Instance,
            new LinuxGlobalHotkeys(),
            new LinuxHostCapture(),
            new LinuxHostWindowing(),
            new LinuxXWayland());
}
