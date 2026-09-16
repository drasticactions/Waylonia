using Waylonia.Shell;

namespace Waylonia;

internal sealed record ConfigValues(
    string? Compress = null,
    bool? Gpu = null,
    bool? Audio = null,
    string? Video = null,
    string? Socket = null,
    string? Command = null,
    string? Terminal = null,
    string? CurrentDesktop = null,
    string Lang = ConfigValues.DefaultLang,
    bool XWayland = true,
    bool Tray = true,
    bool TrayApps = true,
    bool Clipboard = true,
    bool Drag = true,
    bool FollowCursor = true,
    bool GtkDpi = true,
    bool SessionTitles = true,
    string CaptureChord = ConfigValues.DefaultCaptureChord,
    IReadOnlyList<Hotkey>? Hotkeys = null,
    IReadOnlyList<DesktopProfile>? Desktops = null,
    ShellMode Shell = ShellMode.Windows,
    ShellSettings? ShellSettings = null,
    PanelSettings? Panel = null)
{
    public const string DefaultLang = "C.UTF-8";

    public const string DefaultCaptureChord = "double:RightControl";

    public IReadOnlyList<Hotkey> Hotkeys { get; init; } = Hotkeys ?? [];

    public IReadOnlyList<DesktopProfile> Desktops { get; init; } = Desktops ?? [];

    public ShellSettings ShellSettings { get; init; } = ShellSettings ?? new ShellSettings();

    public PanelSettings Panel { get; init; } = Panel ?? new PanelSettings();

    public IReadOnlyList<string> RestartKeysChanged(ConfigValues other)
    {
        ArgumentNullException.ThrowIfNull(other);
        var keys = new List<string>();
        if (XWayland != other.XWayland)
        {
            keys.Add("xwayland");
        }

        if (Shell != other.Shell)
        {
            keys.Add("shell");
        }

        if (Tray != other.Tray)
        {
            keys.Add("tray");
        }

        if (Clipboard != other.Clipboard)
        {
            keys.Add("clipboard");
        }

        if (Drag != other.Drag)
        {
            keys.Add("drag");
        }

        if (FollowCursor != other.FollowCursor)
        {
            keys.Add("follow-cursor");
        }

        if (Socket != other.Socket)
        {
            keys.Add("socket");
        }

        if (Command != other.Command)
        {
            keys.Add("command");
        }

        return keys;
    }
}
