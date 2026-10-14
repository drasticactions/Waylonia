namespace Waylonia;

internal sealed record HostCapabilities(
    bool Tray,
    bool GlobalHotkeys,
    bool KeyboardCapture,
    bool LocalCommands,
    bool LocalDesktops,
    bool XWayland,
    bool LocalApplications,
    bool ChannelsOnly,
    bool SoftKeyboard = false,
    bool KeyImport = false,
    bool Reconnects = false)
{
    public static readonly HostCapabilities None = new(false, false, false, false, false, false, false, true);
}
