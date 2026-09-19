namespace Waylonia.Sessions;

internal sealed record SessionOverrides(
    string? Compress = null,
    bool? Gpu = null,
    bool? Audio = null,
    string? Video = null,
    string? Command = null,
    string? Desktop = null,
    string? DesktopSize = null,
    string AudioFormat = "f32");
