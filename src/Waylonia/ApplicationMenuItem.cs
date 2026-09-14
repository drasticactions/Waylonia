namespace Waylonia;

internal sealed record ApplicationMenuItem(
    string Label,
    string? Command = null,
    IReadOnlyList<ApplicationMenuItem>? Children = null,
    bool Separator = false);
