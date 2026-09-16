using Waylonia.Cli;
using Waylonia.Shell;

namespace Waylonia;

internal static class ConfigWriter
{
    private static readonly string[] HostTable = ["host"];

    private static readonly string[] ShellTable = ["shell"];

    private static readonly string[] ShellKeysTable = ["shell", "keys"];

    private static readonly string[] PanelTable = ["panel"];

    public static string? Save(string path, ConfigValues values)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(values);
        TomlDocument? document;
        string? error = null;
        try
        {
            document = File.Exists(path) ? TomlDocument.Parse(File.ReadAllText(path), out error) : TomlDocument.Empty();
            if (document is null)
            {
                return $"{path} did not parse: {error}";
            }
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return $"{path} cannot be read: {failure.Message}";
        }

        Apply(document, values);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path, document.Render());
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return $"{path} cannot be written: {failure.Message}";
        }

        return null;
    }

    public static void Apply(TomlDocument document, ConfigValues values)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(values);
        var root = document.Root;
        SetText(root, "compress", values.Compress);
        SetFlag(root, "gpu", values.Gpu);
        SetFlag(root, "audio", values.Audio);
        SetText(root, "video", values.Video);
        SetText(root, "socket", values.Socket);
        SetCommand(root, "command", values.Command);
        SetCommand(root, "terminal", values.Terminal);
        SetText(root, "current-desktop", values.CurrentDesktop);
        SetText(root, "lang", values.Lang == ConfigValues.DefaultLang ? null : values.Lang);

        SetToggle(document, "xwayland", values.XWayland);
        SetToggle(document, "tray", values.Tray);
        SetToggle(document, "tray-apps", values.TrayApps);
        SetToggle(document, "clipboard", values.Clipboard);
        SetToggle(document, "drag", values.Drag);
        SetToggle(document, "follow-cursor", values.FollowCursor);
        SetToggle(document, "gtk-dpi", values.GtkDpi);
        SetToggle(document, "session-titles", values.SessionTitles);
        if (values.CaptureChord == ConfigValues.DefaultCaptureChord)
        {
            document.Table("host")?.Remove("capture-chord");
        }
        else
        {
            document.EnsureTable("host").Set("capture-chord", values.CaptureChord);
        }

        SetOrDrop(document, HostTable, "shell", values.Shell == ShellMode.Windows ? null : ShellModes.Name(values.Shell));
        ApplyShell(document, values.ShellSettings);
        ApplyPanel(document, values.Panel);

        var hotkeys = document.Table("hotkeys");
        if (hotkeys is not null)
        {
            foreach (var chord in hotkeys.Keys.ToList())
            {
                if (values.Hotkeys.All(hotkey => hotkey.Chord != chord))
                {
                    hotkeys.Remove(chord);
                }
            }
        }

        foreach (var hotkey in values.Hotkeys)
        {
            if (hotkeys?.Text(hotkey.Chord) != hotkey.Command)
            {
                hotkeys = document.EnsureTable("hotkeys");
                hotkeys.Set(hotkey.Chord, hotkey.Command);
            }
        }

        foreach (var name in document.Tables("desktops"))
        {
            if (values.Desktops.All(desktop => desktop.Name != name))
            {
                document.RemoveTable("desktops", name);
            }
        }

        foreach (var desktop in values.Desktops)
        {
            var section = document.EnsureTable("desktops", desktop.Name);
            SetText(section, "recipe", desktop.Recipe);
            SetText(section, "host", desktop.Host);
            SetText(section, "size", desktop.Size);
            SetCommand(section, "command", desktop.Command);
            if (desktop.Env.Count == 0)
            {
                section.Remove("env");
            }
            else
            {
                section.Set("env", desktop.Env);
            }

            SetFlag(section, "gpu", desktop.Gpu);
            SetText(section, "video", desktop.Video);
        }
    }

    private static void ApplyShell(TomlDocument document, ShellSettings shell)
    {
        var defaults = new ShellSettings();
        SetOrDrop(document, ShellTable, "theme", Kept(shell.Theme, defaults.Theme));
        SetOrDrop(document, ShellTable, "button-layout", Kept(shell.ButtonLayout, defaults.ButtonLayout));
        SetOrDrop(document, ShellTable, "palette", Kept(shell.Palette, defaults.Palette));
        SetOrDrop(document, ShellTable, "font-size", Kept(shell.FontSize, defaults.FontSize));
        SetOrDrop(document, ShellTable, "background", Kept(shell.Background, defaults.Background));
        SetOrDrop(document, ShellTable, "workspaces", Kept(shell.Workspaces, defaults.Workspaces));
        SetOrDrop(document, ShellTable, "workspace-rows", Kept(shell.WorkspaceRows, defaults.WorkspaceRows));
        SetOrDrop(document, ShellTable, "workspace-names", Kept(shell.WorkspaceNames, defaults.WorkspaceNames));
        SetOrDrop(document, ShellTable, "focus-mode", Kept(ShellConfig.Name(shell.FocusMode), ShellConfig.Name(defaults.FocusMode)));
        SetOrDrop(document, ShellTable, "focus-new-windows", Kept(ShellConfig.Name(shell.FocusNewWindows), ShellConfig.Name(defaults.FocusNewWindows)));
        SetOrDrop(document, ShellTable, "placement", Kept(ShellConfig.Name(shell.Placement), ShellConfig.Name(defaults.Placement)));
        SetOrDrop(document, ShellTable, "center-new-windows", Kept(shell.CenterNewWindows, defaults.CenterNewWindows));
        SetOrDrop(document, ShellTable, "raise-on-click", Kept(shell.RaiseOnClick, defaults.RaiseOnClick));
        SetOrDrop(document, ShellTable, "auto-raise", Kept(shell.AutoRaise, defaults.AutoRaise));
        SetOrDrop(document, ShellTable, "auto-raise-delay", Kept(shell.AutoRaiseDelay, defaults.AutoRaiseDelay));
        SetOrDrop(document, ShellTable, "mouse-button-modifier", Kept(shell.MouseButtonModifier, defaults.MouseButtonModifier));
        SetOrDrop(document, ShellTable, "resize-with-right-button", Kept(shell.ResizeWithRightButton, defaults.ResizeWithRightButton));
        SetOrDrop(document, ShellTable, "double-click-titlebar", Kept(ShellConfig.Name(shell.DoubleClickTitlebar), ShellConfig.Name(defaults.DoubleClickTitlebar)));
        SetOrDrop(document, ShellTable, "middle-click-titlebar", Kept(ShellConfig.Name(shell.MiddleClickTitlebar), ShellConfig.Name(defaults.MiddleClickTitlebar)));
        SetOrDrop(document, ShellTable, "right-click-titlebar", Kept(ShellConfig.Name(shell.RightClickTitlebar), ShellConfig.Name(defaults.RightClickTitlebar)));
        SetOrDrop(document, ShellTable, "tiling", Kept(shell.Tiling, defaults.Tiling));
        SetOrDrop(document, ShellTable, "top-tiling", Kept(shell.TopTiling, defaults.TopTiling));

        var keys = document.Table(ShellKeysTable);
        if (keys is not null)
        {
            foreach (var name in keys.Keys.ToList())
            {
                if (shell.Keys.All(key => key.Name != name))
                {
                    keys.Remove(name);
                }
            }
        }

        foreach (var key in shell.Keys)
        {
            if (keys?.Text(key.Name) != key.Chord)
            {
                keys = document.EnsureTable(ShellKeysTable);
                keys.Set(key.Name, key.Chord);
            }
        }

        DropWhenEmpty(document, ShellKeysTable);
        DropWhenEmpty(document, ShellTable);
    }

    private static void ApplyPanel(TomlDocument document, PanelSettings panel)
    {
        var defaults = new PanelSettings();
        SetOrDrop(document, PanelTable, "size", Kept(panel.Size, defaults.Size));
        SetOrDrop(document, PanelTable, "top", Kept(panel.Top, defaults.Top));
        SetOrDrop(document, PanelTable, "bottom", Kept(panel.Bottom, defaults.Bottom));
        DropWhenEmpty(document, PanelTable);
    }

    private static void DropWhenEmpty(TomlDocument document, string[] table)
    {
        if (document.Table(table) is { } section && !section.Keys.Any())
        {
            document.RemoveTable(table);
        }
    }

    private static string? Kept(string value, string fallback) => value == fallback ? null : value;

    private static bool? Kept(bool value, bool fallback) => value == fallback ? null : value;

    private static long? Kept(int value, int fallback) => value == fallback ? null : value;

    private static double? Kept(double value, double fallback) => value == fallback ? null : value;

    private static IReadOnlyList<string>? Kept(IReadOnlyList<string> value, IReadOnlyList<string> fallback) =>
        value.SequenceEqual(fallback) ? null : value;

    private static void SetOrDrop(TomlDocument document, string[] table, string key, string? value)
    {
        if (value is null)
        {
            document.Table(table)?.Remove(key);
        }
        else
        {
            document.EnsureTable(table).Set(key, value);
        }
    }

    private static void SetOrDrop(TomlDocument document, string[] table, string key, bool? value)
    {
        if (value is { } flag)
        {
            document.EnsureTable(table).Set(key, flag);
        }
        else
        {
            document.Table(table)?.Remove(key);
        }
    }

    private static void SetOrDrop(TomlDocument document, string[] table, string key, long? value)
    {
        if (value is { } number)
        {
            document.EnsureTable(table).Set(key, number);
        }
        else
        {
            document.Table(table)?.Remove(key);
        }
    }

    private static void SetOrDrop(TomlDocument document, string[] table, string key, double? value)
    {
        if (value is { } number)
        {
            document.EnsureTable(table).Set(key, number);
        }
        else
        {
            document.Table(table)?.Remove(key);
        }
    }

    private static void SetOrDrop(TomlDocument document, string[] table, string key, IReadOnlyList<string>? value)
    {
        if (value is null)
        {
            document.Table(table)?.Remove(key);
        }
        else
        {
            document.EnsureTable(table).Set(key, value);
        }
    }

    private static void SetToggle(TomlDocument document, string key, bool value)
    {
        if (value)
        {
            document.Table("host")?.Remove(key);
        }
        else
        {
            document.EnsureTable("host").Set(key, false);
        }
    }

    private static void SetText(TomlSection section, string key, string? value)
    {
        if (value is null)
        {
            section.Remove(key);
        }
        else
        {
            section.Set(key, value);
        }
    }

    private static void SetCommand(TomlSection section, string key, string? value)
    {
        if (value is null)
        {
            section.Remove(key);
        }
        else if (section.Text(key) != value)
        {
            section.Set(key, value);
        }
    }

    private static void SetFlag(TomlSection section, string key, bool? value)
    {
        if (value is { } flag)
        {
            section.Set(key, flag);
        }
        else
        {
            section.Remove(key);
        }
    }
}
