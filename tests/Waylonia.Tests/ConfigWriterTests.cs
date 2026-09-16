using Basin.Diagnostics;
using Waylonia.Cli;
using Waylonia.Shell;
using Xunit;

namespace Waylonia.Tests;

public sealed class ConfigWriterTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "waylonia-writer-" + Guid.NewGuid().ToString("n"));

    private string PathOf(string? text)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "waylonia.toml");
        if (text is not null)
        {
            File.WriteAllText(path, text);
        }

        return path;
    }

    private static string Apply(string text, ConfigValues values)
    {
        var document = TomlDocument.Parse(text, out var error);
        Assert.Null(error);
        ConfigWriter.Apply(document!, values);
        return document!.Render();
    }

    [Fact]
    public void Values_at_their_defaults_write_nothing()
    {
        var document = TomlDocument.Empty();
        ConfigWriter.Apply(document, new ConfigValues());
        Assert.Equal(string.Empty, document.Render());
    }

    [Fact]
    public void Every_value_lands_in_the_file_and_loads_back()
    {
        var path = PathOf(null);
        var values = new ConfigValues(
            "zstd", true, false, "h264,hw", "wayland-9", "foot", "foot -e", "KDE", "en_US.UTF-8",
            false, false, false, false, false, false, false, false, 45, "RightAlt",
            [Hotkey.Parse("ctrl+alt+t", "foot", BasinLogger.None)!],
            [new DesktopProfile("lab", "sway", "devbox", "1920x1080", "sway --unsupported-gpu", ["A=1", "B=2"], false, "none")]);

        Assert.Null(ConfigWriter.Save(path, values));

        var config = Config.Load(false, path, BasinLogger.None);
        Assert.Equal("zstd", config.Compress);
        Assert.True(config.Gpu);
        Assert.False(config.Audio);
        Assert.Equal("h264,hw", config.Video);
        Assert.Equal("wayland-9", config.Socket);
        Assert.Equal("foot", config.Command);
        Assert.Equal("foot -e", config.Terminal);
        Assert.Equal("KDE", config.CurrentDesktop);
        Assert.Equal("en_US.UTF-8", config.Lang);
        Assert.False(config.XWayland);
        Assert.False(config.Tray);
        Assert.False(config.TrayApps);
        Assert.False(config.Clipboard);
        Assert.False(config.Drag);
        Assert.False(config.FollowCursor);
        Assert.False(config.GtkDpi);
        Assert.False(config.SessionTitles);
        Assert.Equal(45, config.SshTimeout);
        Assert.Equal("RightAlt", config.CaptureChord);
        var hotkey = Assert.Single(config.Hotkeys);
        Assert.Equal(("ctrl+alt+t", "foot"), (hotkey.Chord, hotkey.Command));
        var desktop = Assert.Single(config.Desktops.Values);
        Assert.Equal(values.Desktops[0], desktop with { Env = values.Desktops[0].Env });
        Assert.Equal(["A=1", "B=2"], desktop.Env);
    }

    [Fact]
    public void An_empty_lang_is_written_as_an_empty_string_and_the_default_is_dropped()
    {
        Assert.Equal("lang = \"\"\n", Apply(string.Empty, new ConfigValues(Lang: string.Empty)));
        Assert.Equal(string.Empty, Apply("lang = \"C.UTF-8\"\n", new ConfigValues()));
    }

    [Fact]
    public void A_toggle_back_at_its_default_loses_its_key_and_the_chord_too()
    {
        var text = Apply("[host]\nclipboard = false\ncapture-chord = \"RightAlt\"\n", new ConfigValues());
        Assert.Equal("[host]\n", text);
    }

    [Fact]
    public void The_placeholder_keeps_its_comments_and_the_new_keys_load_back()
    {
        var path = PathOf(null);
        Config.WritePlaceholder(path, BasinLogger.None);
        var placeholder = File.ReadAllText(path);

        Assert.Null(ConfigWriter.Save(path, new ConfigValues(Compress: "none", Tray: false)));

        var text = File.ReadAllText(path);
        Assert.StartsWith(placeholder, text, StringComparison.Ordinal);
        Assert.EndsWith("compress = \"none\"\n\n[host]\ntray = false\n", text, StringComparison.Ordinal);
        var config = Config.Load(false, path, BasinLogger.None);
        Assert.Equal("none", config.Compress);
        Assert.False(config.Tray);
    }

    [Fact]
    public void A_hotkey_dropped_from_the_list_goes_and_the_others_keep_their_comments()
    {
        var text = Apply(
            "[hotkeys]\n# terminal\n\"ctrl+alt+t\" = \"foot\"\n# browser\n\"ctrl+alt+b\" = [\"firefox\"]\n",
            new ConfigValues(Hotkeys:
            [
                Hotkey.Parse("ctrl+alt+b", "firefox", BasinLogger.None)!,
                Hotkey.Parse("ctrl+alt+m", "thunderbird", BasinLogger.None)!,
            ]));

        Assert.Equal(
            "[hotkeys]\n# terminal\n# browser\n\"ctrl+alt+b\" = [\"firefox\"]\n\"ctrl+alt+m\" = \"thunderbird\"\n",
            text);
    }

    [Fact]
    public void A_command_array_is_left_alone_while_its_text_is_unchanged()
    {
        const string text = "command = [\"tmux\", \"new\"]\nterminal = \"foot -e\"\n";
        Assert.Equal(text, Apply(text, new ConfigValues(Command: "tmux new", Terminal: "foot -e")));
        Assert.Equal("command = \"tmux attach\"\nterminal = \"foot -e\"\n", Apply(text, new ConfigValues(Command: "tmux attach", Terminal: "foot -e")));
    }

    [Fact]
    public void A_desktop_dropped_from_the_list_loses_its_table_and_an_edited_one_keeps_its_place()
    {
        var text = Apply(
            "[desktops.lab]\nrecipe = \"sway\"\n\n[desktops.kde]\n# big\nsize = \"1920x1080\"\nrecipe = \"plasma\"\nenv = [\"A=1\"]\n",
            new ConfigValues(Desktops: [new DesktopProfile("kde", "plasma", "devbox", null, null, [], true, null)]));

        Assert.Equal("[desktops.kde]\n# big\nrecipe = \"plasma\"\nhost = \"devbox\"\ngpu = true\n", text);
    }

    private static ShellSettings EveryShellValue() => new(
        "Crux",
        "close,minimize,maximize:menu",
        "dark",
        11.5,
        "#023c88",
        6,
        2,
        ["Main", "Mail"],
        FocusMode.Sloppy,
        FocusNewWindows.Strict,
        PlacementMode.Pointer,
        false,
        false,
        true,
        250,
        "Super",
        false,
        TitlebarAction.ToggleShade,
        TitlebarAction.None,
        TitlebarAction.Lower,
        false,
        false,
        [new ShellKey("close", "Super+q"), new ShellKey("minimize", ""), new ShellKey("tile-to-side-w", "Super+Left")]);

    [Fact]
    public void Every_shell_and_panel_value_lands_in_the_file_and_loads_back()
    {
        var path = PathOf(null);
        var shell = EveryShellValue();
        var panel = new PanelSettings(32, ["menu-bar", "spacer", "clock"], []);

        Assert.Null(ConfigWriter.Save(path, new ConfigValues(Shell: ShellMode.Nested, ShellSettings: shell, Panel: panel)));

        var config = Config.Load(false, path, BasinLogger.None);
        Assert.Equal(ShellMode.Nested, config.Shell);
        Assert.Equal(shell, config.ShellSettings with { WorkspaceNames = shell.WorkspaceNames, Keys = shell.Keys });
        Assert.Equal(shell.WorkspaceNames, config.ShellSettings.WorkspaceNames);
        Assert.Equal(shell.Keys, config.ShellSettings.Keys);
        Assert.Equal(32, config.Panel.Size);
        Assert.Equal(["menu-bar", "spacer", "clock"], config.Panel.Top);
        Assert.Empty(config.Panel.Bottom);
    }

    [Fact]
    public void The_shell_tables_are_written_in_full_and_a_second_save_changes_nothing()
    {
        var text = Apply(string.Empty, new ConfigValues(Shell: ShellMode.Nested, ShellSettings: EveryShellValue(), Panel: new PanelSettings(32, ["menu-bar", "spacer", "clock"], [])));

        Assert.Equal("""

            [host]
            shell = "nested"

            [shell]
            theme = "Crux"
            button-layout = "close,minimize,maximize:menu"
            palette = "dark"
            font-size = 11.5
            background = "#023c88"
            workspaces = 6
            workspace-rows = 2
            workspace-names = ["Main", "Mail"]
            focus-mode = "sloppy"
            focus-new-windows = "strict"
            placement = "pointer"
            center-new-windows = false
            raise-on-click = false
            auto-raise = true
            auto-raise-delay = 250
            mouse-button-modifier = "Super"
            resize-with-right-button = false
            double-click-titlebar = "toggle_shade"
            middle-click-titlebar = "none"
            right-click-titlebar = "lower"
            tiling = false
            top-tiling = false

            [shell.keys]
            close = "Super+q"
            minimize = ""
            tile-to-side-w = "Super+Left"

            [panel]
            size = 32
            top = ["menu-bar", "spacer", "clock"]
            bottom = []

            """, text);
        Assert.Equal(text, Apply(text, new ConfigValues(Shell: ShellMode.Nested, ShellSettings: EveryShellValue(), Panel: new PanelSettings(32, ["menu-bar", "spacer", "clock"], []))));
    }

    [Fact]
    public void Shell_values_back_at_their_defaults_lose_their_keys_and_the_emptied_tables_go()
    {
        var text = Apply(
            """
            [host]
            shell = "nested"

            # frames
            [shell]
            theme = "Crux"
            font-size = 13
            workspaces = 4

            [shell.keys]
            close = "Super+q"

            [panel]
            size = 24
            top = ["menu-bar"]

            # end
            """,
            new ConfigValues());

        Assert.Equal("[host]\n\n# frames\n\n# end", text);
        Assert.NotNull(TomlDocument.Parse(text, out _));
    }

    [Fact]
    public void A_shell_key_dropped_from_the_list_goes_and_the_others_keep_their_comments()
    {
        var text = Apply(
            "[shell]\n# the frame theme\ntheme = \"Crux\"\n\n[shell.keys]\n# quit\nclose = \"Super+q\"\n# hide\nminimize = \"Super+h\"\n",
            new ConfigValues(ShellSettings: new ShellSettings(Theme: "Crux", Keys: [new ShellKey("minimize", "Super+h"), new ShellKey("lower", "Super+l")])));

        Assert.Equal(
            "[shell]\n# the frame theme\ntheme = \"Crux\"\n\n[shell.keys]\n# quit\n# hide\nminimize = \"Super+h\"\nlower = \"Super+l\"\n",
            text);
    }

    [Fact]
    public void A_whole_font_size_is_written_as_an_integer_and_an_unchanged_one_is_left_alone()
    {
        const string text = "[shell]\nfont-size = 14.0 # points\n";
        Assert.Equal(text, Apply(text, new ConfigValues(ShellSettings: new ShellSettings(FontSize: 14))));
        Assert.Equal("[shell]\nfont-size = 15 # points\n", Apply(text, new ConfigValues(ShellSettings: new ShellSettings(FontSize: 15))));
        Assert.Equal("[shell]\nfont-size = 12.5 # points\n", Apply(text, new ConfigValues(ShellSettings: new ShellSettings(FontSize: 12.5))));
    }

    [Fact]
    public void The_host_shell_key_is_written_for_nested_and_removed_for_windows()
    {
        Assert.Equal("\n[host]\nshell = \"nested\"\n", Apply(string.Empty, new ConfigValues(Shell: ShellMode.Nested)));
        Assert.Equal("[host]\ntray = false\n", Apply("[host]\ntray = false\nshell = \"nested\"\n", new ConfigValues(Tray: false)));
    }

    [Fact]
    public void The_placeholder_keeps_its_comments_around_the_new_shell_tables()
    {
        var path = PathOf(null);
        Config.WritePlaceholder(path, BasinLogger.None);
        var placeholder = File.ReadAllText(path);

        Assert.Null(ConfigWriter.Save(path, new ConfigValues(
            Shell: ShellMode.Nested,
            ShellSettings: new ShellSettings(Workspaces: 2, Keys: [new ShellKey("close", "Super+q")]),
            Panel: new PanelSettings(Bottom: []))));

        var text = File.ReadAllText(path);
        Assert.StartsWith(placeholder, text, StringComparison.Ordinal);
        Assert.EndsWith(
            "[host]\nshell = \"nested\"\n\n[shell]\nworkspaces = 2\n\n[shell.keys]\nclose = \"Super+q\"\n\n[panel]\nbottom = []\n",
            text,
            StringComparison.Ordinal);
        var config = Config.Load(false, path, BasinLogger.None);
        Assert.Equal(ShellMode.Nested, config.Shell);
        Assert.Equal(2, config.ShellSettings.Workspaces);
        Assert.Equal([new ShellKey("close", "Super+q")], config.ShellSettings.Keys);
        Assert.Empty(config.Panel.Bottom);
    }

    [Fact]
    public void A_broken_file_is_reported_and_left_alone()
    {
        var path = PathOf("compress = \n");
        var error = ConfigWriter.Save(path, new ConfigValues(Compress: "none"));

        Assert.NotNull(error);
        Assert.Contains("did not parse", error, StringComparison.Ordinal);
        Assert.Equal("compress = \n", File.ReadAllText(path));
    }

    [Fact]
    public void Restart_keys_are_the_ones_the_host_reads_once()
    {
        var running = new ConfigValues();
        Assert.Empty(running.RestartKeysChanged(new ConfigValues(SessionTitles: false, Hotkeys: [Hotkey.Parse("ctrl+t", "foot", BasinLogger.None)!])));
        Assert.Equal(
            ["xwayland", "tray", "clipboard", "drag", "follow-cursor", "socket", "command"],
            running.RestartKeysChanged(new ConfigValues(
                XWayland: false, Tray: false, Clipboard: false, Drag: false, FollowCursor: false, Socket: "wayland-9", Command: "foot")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
