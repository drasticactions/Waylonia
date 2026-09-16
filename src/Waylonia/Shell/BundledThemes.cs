using Basin.Frames.Metacity;

namespace Waylonia.Shell;

internal static class BundledThemes
{
    private const string Prefix = "Waylonia.Themes.";

    public static IReadOnlyList<string> Names { get; } = ["Atlanta"];

    public static MetacityTheme? Load(string name)
    {
        foreach (var bundled in Names)
        {
            if (!string.Equals(bundled, name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            using var stream = typeof(BundledThemes).Assembly.GetManifestResourceStream(
                $"{Prefix}{bundled}.metacity-theme-1.xml");
            return stream is null ? null : MetacityTheme.Parse(stream, directory: string.Empty, majorVersion: 1, name: bundled);
        }

        return null;
    }
}
