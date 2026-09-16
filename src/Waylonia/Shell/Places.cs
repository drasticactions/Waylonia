namespace Waylonia.Shell;

internal static class Places
{
    public static IReadOnlyList<(string Label, string Path)> Entries { get; } =
    [
        ("Home", "~"),
        ("Desktop", "~/Desktop"),
        ("Documents", "~/Documents"),
        ("Downloads", "~/Downloads"),
        ("Pictures", "~/Pictures"),
        ("Videos", "~/Videos"),
        ("Music", "~/Music"),
        ("Trash", "trash:///"),
    ];
}
