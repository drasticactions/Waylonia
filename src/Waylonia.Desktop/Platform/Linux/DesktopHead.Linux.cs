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

    private static partial DesktopPlatform Compose(
        HostCapabilities capabilities,
        WayloniaPaths paths,
        ISshLinkFactory links,
        Func<AudioFill, int, int, IAudioSink?> audio) => new(
            new HostPlatform(
                capabilities,
                paths,
                links,
                audio,
                Video,
                new LinuxHostCursor(),
                NullHostScreenScales.Instance,
                new LinuxHostWindowing(),
                NullKeyPicker.Instance),
            new LinuxGlobalHotkeys(),
            new LinuxHostCapture(),
            new LinuxXWayland());
}
