namespace Waylonia;

internal sealed record HostSettings(
    bool XWayland = true,
    bool Tray = true,
    bool TrayApps = true,
    bool Clipboard = true,
    bool Drag = true,
    bool FollowCursor = true,
    bool GtkDpi = true,
    string CaptureChord = "double:RightControl",
    bool SessionTitles = true,
    IReadOnlyList<Hotkey>? Hotkeys = null,
    string? Terminal = null,
    string? CurrentDesktop = null)
{
    public IReadOnlyList<Hotkey> Hotkeys { get; init; } = Hotkeys ?? [];
}
