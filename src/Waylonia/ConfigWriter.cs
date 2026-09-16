using Waylonia.Cli;

namespace Waylonia;

internal static class ConfigWriter
{
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
