namespace Waylonia.Shell;

internal static class ShellModes
{
    public const string Names = "windows or nested";

    public static ShellMode? Parse(string text) => text.Trim().ToLowerInvariant() switch
    {
        "windows" => ShellMode.Windows,
        "nested" => ShellMode.Nested,
        _ => null,
    };

    public static string Name(ShellMode mode) => mode == ShellMode.Nested ? "nested" : "windows";
}
