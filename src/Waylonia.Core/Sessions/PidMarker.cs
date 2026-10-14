using System.Globalization;

namespace Waylonia.Sessions;

internal static class PidMarker
{
    public const string Prefix = "waylonia-pid ";

    public const string Wrapper = $"echo \"{Prefix}$$\" >&2; exec sh -c \"$1\"";

    public static int? Parse(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        return line.StartsWith(Prefix, StringComparison.Ordinal)
            && int.TryParse(line.AsSpan(Prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var pid)
            ? pid
            : null;
    }
}
