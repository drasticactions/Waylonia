using System.Globalization;
using Basin;

namespace Waylonia.Shell;

internal static class ShellColor
{
    public const string Format = "#rrggbb or #rgb";

    public static string? Normalize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var trimmed = text.Trim();
        if (trimmed.Length is not (4 or 7) || trimmed[0] != '#' || !trimmed.Skip(1).All(Uri.IsHexDigit))
        {
            return null;
        }

        return trimmed.Length == 4
            ? string.Create(CultureInfo.InvariantCulture, $"#{trimmed[1]}{trimmed[1]}{trimmed[2]}{trimmed[2]}{trimmed[3]}{trimmed[3]}").ToLowerInvariant()
            : trimmed.ToLowerInvariant();
    }

    public static RenderColor? Parse(string text)
    {
        if (Normalize(text) is not { } hex)
        {
            return null;
        }

        return new RenderColor(Channel(hex, 1), Channel(hex, 3), Channel(hex, 5), 1f);
    }

    private static float Channel(string hex, int start) =>
        int.Parse(hex.AsSpan(start, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255f;
}
