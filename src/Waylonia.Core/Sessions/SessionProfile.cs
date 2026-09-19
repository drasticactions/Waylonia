namespace Waylonia.Sessions;

internal sealed record SessionProfile(
    string Name,
    string Ssh,
    string? Command = null,
    IReadOnlyList<string>? Autostart = null,
    string? Compress = null,
    bool? Gpu = null,
    string? Video = null,
    bool? Audio = null,
    string? Terminal = null,
    string? CurrentDesktop = null,
    string? Lang = null,
    bool Autoconnect = false,
    string? Desktop = null,
    string? DesktopSize = null,
    IReadOnlyList<string>? DesktopEnv = null,
    IReadOnlyList<Hotkey>? Hotkeys = null);
