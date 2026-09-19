using System.Globalization;

namespace Waylonia.Cli;

public static class VideoChoice
{
    private const string BitsPerFrame = "bpf=";

    private static readonly string[] Codecs = ["none", "h264", "vp9", "av1"];

    public static bool IsValid(string? value)
    {
        if (value is null)
        {
            return false;
        }

        var parts = value.Split(',');
        if (!Codecs.Contains(parts[0], StringComparer.Ordinal))
        {
            return false;
        }

        if (parts[0] == "none")
        {
            return parts.Length == 1;
        }

        var seen = 0;
        for (var i = 1; i < parts.Length; i++)
        {
            var group = GroupOf(parts[i]);
            if (group == 0 || (seen & group) != 0)
            {
                return false;
            }

            seen |= group;
        }

        return true;
    }

    public static bool DecodesOnGpu(string? value) =>
        value is not null && value.Split(',').Contains("hw", StringComparer.Ordinal);

    public static string? RemoteSetting(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var travelling = value.Split(',').Where(Travels).ToArray();
        return travelling.Length == 0 ? null : string.Join(',', travelling);
    }

    private static bool Travels(string part) =>
        part is "hwenc" or "swenc" or "hwdec" or "swdec"
        || part.StartsWith(BitsPerFrame, StringComparison.Ordinal);

    private static int GroupOf(string part)
    {
        if (part == "hw")
        {
            return 1;
        }

        if (part is "hwenc" or "swenc")
        {
            return 2;
        }

        if (part is "hwdec" or "swdec")
        {
            return 4;
        }

        return part.StartsWith(BitsPerFrame, StringComparison.Ordinal) && IsRate(part[BitsPerFrame.Length..])
            ? 8
            : 0;
    }

    private static bool IsRate(string text) =>
        float.TryParse(
            text,
            NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent,
            CultureInfo.InvariantCulture,
            out var rate)
        && float.IsFinite(rate)
        && rate > 0;
}
