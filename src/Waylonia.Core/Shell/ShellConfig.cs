using System.Globalization;
using Basin.Diagnostics;
using Tomlyn.Model;

namespace Waylonia.Shell;

internal static class ShellConfig
{
    public const int MaxWorkspaces = 36;

    public const string FocusModeNames = "click, sloppy or mouse";

    public const string FocusNewWindowsNames = "smart or strict";

    public const string PlacementNames = "automatic, pointer or manual";

    public const string PaletteNames = "light or dark";

    public const string MouseButtonModifierNames = "Alt, Super or Ctrl";

    public const string TitlebarActionNames =
        "none, toggle_shade, toggle_maximize, toggle_maximize_horizontally, toggle_maximize_vertically, minimize, lower or menu";

    public const string ButtonNames = "menu, appmenu, minimize, maximize, close, shade, above, stick and spacer";

    private static readonly string[] Buttons =
        ["menu", "appmenu", "minimize", "maximize", "close", "shade", "above", "stick", "spacer"];

    public static ShellSettings Parse(TomlTable table, BasinLogger log)
    {
        ArgumentNullException.ThrowIfNull(table);
        var defaults = new ShellSettings();
        var workspaces = Integer(table, "workspaces", 1, MaxWorkspaces, defaults.Workspaces, log);
        return new ShellSettings(
            Text(table, "theme", log) ?? defaults.Theme,
            ButtonLayout(table, log) ?? defaults.ButtonLayout,
            Choice(table, "palette", defaults.Palette, ParsePalette, PaletteNames, log),
            FontSize(table, defaults.FontSize, log),
            Background(table, defaults.Background, log),
            workspaces,
            Integer(table, "workspace-rows", 1, workspaces, Math.Min(defaults.WorkspaceRows, workspaces), log),
            Strings(table, "workspace-names", defaults.WorkspaceNames, log),
            Choice(table, "focus-mode", defaults.FocusMode, ParseFocusMode, FocusModeNames, log),
            Choice(table, "focus-new-windows", defaults.FocusNewWindows, ParseFocusNewWindows, FocusNewWindowsNames, log),
            Choice(table, "placement", defaults.Placement, ParsePlacement, PlacementNames, log),
            Flag(table, "center-new-windows", defaults.CenterNewWindows, log),
            Flag(table, "raise-on-click", defaults.RaiseOnClick, log),
            Flag(table, "auto-raise", defaults.AutoRaise, log),
            Integer(table, "auto-raise-delay", 0, int.MaxValue, defaults.AutoRaiseDelay, log),
            Choice(table, "mouse-button-modifier", defaults.MouseButtonModifier, MouseButtonModifier, MouseButtonModifierNames, log),
            Flag(table, "resize-with-right-button", defaults.ResizeWithRightButton, log),
            Choice(table, "double-click-titlebar", defaults.DoubleClickTitlebar, ParseTitlebarAction, TitlebarActionNames, log),
            Choice(table, "middle-click-titlebar", defaults.MiddleClickTitlebar, ParseTitlebarAction, TitlebarActionNames, log),
            Choice(table, "right-click-titlebar", defaults.RightClickTitlebar, ParseTitlebarAction, TitlebarActionNames, log),
            Flag(table, "tiling", defaults.Tiling, log),
            Flag(table, "top-tiling", defaults.TopTiling, log),
            Keys(table, log));
    }

    public static FocusMode? ParseFocusMode(string text) => text.Trim().ToLowerInvariant() switch
    {
        "click" => FocusMode.Click,
        "sloppy" => FocusMode.Sloppy,
        "mouse" => FocusMode.Mouse,
        _ => null,
    };

    public static string Name(FocusMode mode) => mode switch
    {
        FocusMode.Sloppy => "sloppy",
        FocusMode.Mouse => "mouse",
        _ => "click",
    };

    public static FocusNewWindows? ParseFocusNewWindows(string text) => text.Trim().ToLowerInvariant() switch
    {
        "smart" => FocusNewWindows.Smart,
        "strict" => FocusNewWindows.Strict,
        _ => null,
    };

    public static string Name(FocusNewWindows mode) => mode == FocusNewWindows.Strict ? "strict" : "smart";

    public static PlacementMode? ParsePlacement(string text) => text.Trim().ToLowerInvariant() switch
    {
        "automatic" => PlacementMode.Automatic,
        "pointer" => PlacementMode.Pointer,
        "manual" => PlacementMode.Manual,
        _ => null,
    };

    public static string Name(PlacementMode mode) => mode switch
    {
        PlacementMode.Pointer => "pointer",
        PlacementMode.Manual => "manual",
        _ => "automatic",
    };

    public static TitlebarAction? ParseTitlebarAction(string text) => text.Trim().ToLowerInvariant() switch
    {
        "none" => TitlebarAction.None,
        "toggle_shade" => TitlebarAction.ToggleShade,
        "toggle_maximize" => TitlebarAction.ToggleMaximize,
        "toggle_maximize_horizontally" => TitlebarAction.ToggleMaximizeHorizontally,
        "toggle_maximize_vertically" => TitlebarAction.ToggleMaximizeVertically,
        "minimize" => TitlebarAction.Minimize,
        "lower" => TitlebarAction.Lower,
        "menu" => TitlebarAction.Menu,
        _ => null,
    };

    public static string Name(TitlebarAction action) => action switch
    {
        TitlebarAction.ToggleShade => "toggle_shade",
        TitlebarAction.ToggleMaximize => "toggle_maximize",
        TitlebarAction.ToggleMaximizeHorizontally => "toggle_maximize_horizontally",
        TitlebarAction.ToggleMaximizeVertically => "toggle_maximize_vertically",
        TitlebarAction.Minimize => "minimize",
        TitlebarAction.Lower => "lower",
        TitlebarAction.Menu => "menu",
        _ => "none",
    };

    public static string? ParsePalette(string text) => text.Trim().ToLowerInvariant() switch
    {
        "light" => "light",
        "dark" => "dark",
        _ => null,
    };

    public static string? MouseButtonModifier(string text) => text.Trim().ToLowerInvariant() switch
    {
        "alt" => "Alt",
        "super" => "Super",
        "ctrl" => "Ctrl",
        _ => null,
    };

    public static bool IsButtonLayout(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var sides = text.Split(':');
        return sides.Length <= 2
            && sides.All(static side => side.Split(',').All(static name => name.Length == 0 || Buttons.Contains(name)));
    }

    private static string Background(TomlTable table, string fallback, BasinLogger log)
    {
        if (!table.TryGetValue("background", out var value))
        {
            return fallback;
        }

        if (value is string text && ShellColor.Normalize(text) is { } color)
        {
            return color;
        }

        log.Warn($"[shell] background takes a color as {ShellColor.Format}, ignoring '{Show(value)}'");
        return fallback;
    }

    private static string? Text(TomlTable table, string key, BasinLogger log)
    {
        if (!table.TryGetValue(key, out var value))
        {
            return null;
        }

        if (value is string text && text.Trim().Length > 0)
        {
            return text.Trim();
        }

        log.Warn($"[shell] {key} takes a name, ignoring '{Show(value)}'");
        return null;
    }

    private static string? ButtonLayout(TomlTable table, BasinLogger log)
    {
        if (!table.TryGetValue("button-layout", out var value))
        {
            return null;
        }

        if (value is string text && IsButtonLayout(text.Trim()))
        {
            return text.Trim();
        }

        log.Warn($"[shell] button-layout takes {ButtonNames} around one ':', ignoring '{Show(value)}'");
        return null;
    }

    private static T Choice<T>(TomlTable table, string key, T fallback, Func<string, T?> parse, string names, BasinLogger log)
        where T : struct
    {
        if (!table.TryGetValue(key, out var value))
        {
            return fallback;
        }

        if (value is string text && parse(text) is { } parsed)
        {
            return parsed;
        }

        log.Warn($"[shell] {key} takes {names}, ignoring '{Show(value)}'");
        return fallback;
    }

    private static string Choice(TomlTable table, string key, string fallback, Func<string, string?> parse, string names, BasinLogger log)
    {
        if (!table.TryGetValue(key, out var value))
        {
            return fallback;
        }

        if (value is string text && parse(text) is { } parsed)
        {
            return parsed;
        }

        log.Warn($"[shell] {key} takes {names}, ignoring '{Show(value)}'");
        return fallback;
    }

    private static bool Flag(TomlTable table, string key, bool fallback, BasinLogger log)
    {
        if (!table.TryGetValue(key, out var value))
        {
            return fallback;
        }

        if (value is bool flag)
        {
            return flag;
        }

        log.Warn($"[shell] {key} takes true or false, ignoring '{Show(value)}'");
        return fallback;
    }

    private static int Integer(TomlTable table, string key, int min, int max, int fallback, BasinLogger log)
    {
        if (!table.TryGetValue(key, out var value))
        {
            return fallback;
        }

        if (value is long number && number >= min && number <= max)
        {
            return (int)number;
        }

        var range = max == int.MaxValue ? $"a whole number of at least {min}" : $"{min} to {max}";
        log.Warn($"[shell] {key} takes {range}, ignoring '{Show(value)}'");
        return fallback;
    }

    private static double FontSize(TomlTable table, double fallback, BasinLogger log)
    {
        if (!table.TryGetValue("font-size", out var value))
        {
            return fallback;
        }

        var size = value switch
        {
            long whole => whole,
            double number => number,
            _ => double.NaN,
        };
        if (size > 0 && double.IsFinite(size))
        {
            return size;
        }

        log.Warn($"[shell] font-size takes a positive number, ignoring '{Show(value)}'");
        return fallback;
    }

    private static IReadOnlyList<string> Strings(TomlTable table, string key, IReadOnlyList<string> fallback, BasinLogger log)
    {
        if (!table.TryGetValue(key, out var value))
        {
            return fallback;
        }

        if (value is TomlArray array && array.All(static item => item is string))
        {
            return array.Cast<string>().ToArray();
        }

        log.Warn($"[shell] {key} takes an array of strings, ignoring it");
        return fallback;
    }

    private static IReadOnlyList<ShellKey> Keys(TomlTable table, BasinLogger log)
    {
        if (!table.TryGetValue("keys", out var value) || value is not TomlTable keys)
        {
            return [];
        }

        var parsed = new List<ShellKey>();
        foreach (var (name, chord) in keys)
        {
            if (!ShellKeyNames.IsKnown(name))
            {
                log.Warn($"[shell.keys] {name} is not a key, ignoring it; the keys are {ShellKeyNames.Listed}");
                continue;
            }

            if (chord is not string text)
            {
                log.Warn($"[shell.keys] {name} takes a chord such as Alt+F4, ignoring '{Show(chord)}'");
                continue;
            }

            text = text.Trim();
            if (text.Length > 0 && KeyTable.ChordProblem(text, out _, out _) is { } problem)
            {
                log.Warn($"[shell.keys] {name}: {problem}, ignoring it");
                continue;
            }

            parsed.Add(new ShellKey(name, text));
        }

        return parsed;
    }

    internal static string Show(object? value) => value switch
    {
        null => string.Empty,
        string text => text,
        bool flag => flag ? "true" : "false",
        long number => number.ToString(CultureInfo.InvariantCulture),
        double number => number.ToString(CultureInfo.InvariantCulture),
        TomlArray array => "[" + string.Join(", ", array.Select(Show)) + "]",
        _ => value.ToString() ?? string.Empty,
    };
}
