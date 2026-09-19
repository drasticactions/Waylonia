using Basin.Diagnostics;
using Tomlyn.Model;

namespace Waylonia.Shell;

internal static class PanelConfig
{
    public const int MinSize = 8;

    public const int MaxSize = 128;

    public static PanelSettings Parse(TomlTable table, BasinLogger log)
    {
        ArgumentNullException.ThrowIfNull(table);
        var defaults = new PanelSettings();
        return new PanelSettings(
            Size(table, defaults.Size, log),
            Applets(table, "top", defaults.Top, log),
            Applets(table, "bottom", defaults.Bottom, log));
    }

    private static int Size(TomlTable table, int fallback, BasinLogger log)
    {
        if (!table.TryGetValue("size", out var value))
        {
            return fallback;
        }

        if (value is long number && number >= MinSize && number <= MaxSize)
        {
            return (int)number;
        }

        log.Warn($"[panel] size takes {MinSize} to {MaxSize}, ignoring '{ShellConfig.Show(value)}'");
        return fallback;
    }

    private static IReadOnlyList<string> Applets(TomlTable table, string key, IReadOnlyList<string> fallback, BasinLogger log)
    {
        if (!table.TryGetValue(key, out var value))
        {
            return fallback;
        }

        if (value is TomlArray array && array.All(static item => item is string))
        {
            return array.Cast<string>().Select(static name => name.Trim()).ToArray();
        }

        log.Warn($"[panel] {key} takes an array of applet names, ignoring it; the applets are {PanelLayout.AppletNames}");
        return fallback;
    }
}
