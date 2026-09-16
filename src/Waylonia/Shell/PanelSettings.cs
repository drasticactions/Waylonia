namespace Waylonia.Shell;

internal sealed record PanelSettings(
    int Size = PanelSettings.DefaultSize,
    IReadOnlyList<string>? Top = null,
    IReadOnlyList<string>? Bottom = null)
{
    public const int DefaultSize = 24;

    public static IReadOnlyList<string> DefaultTop { get; } = ["menu-bar"];

    public static IReadOnlyList<string> DefaultBottom { get; } = ["window-list", "workspace-switcher"];

    public IReadOnlyList<string> Top { get; init; } = Top ?? DefaultTop;

    public IReadOnlyList<string> Bottom { get; init; } = Bottom ?? DefaultBottom;
}
