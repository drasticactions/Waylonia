using Basin.Diagnostics;
using Basin.Shell.Nested;
using Waylonia.Shell;
using Xunit;

namespace Waylonia.Tests;

[Collection(LogCaptureCollection.Name)]
public sealed class ShellConfigTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "waylonia-shell-" + Guid.NewGuid().ToString("n"));

    private string Write(string toml)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "waylonia.toml");
        File.WriteAllText(path, toml);
        return path;
    }

    private Config Load(string toml) => Config.Load(WayloniaPaths.ConfigOnly(Write(toml)), BasinLogger.None);

    private Config LoadCapturing(string toml, out List<string> warnings)
    {
        using var capture = new LogCapture();
        warnings = capture.Lines;
        return Config.Load(WayloniaPaths.ConfigOnly(Write(toml)), BasinLog.For("test"));
    }

    [Fact]
    public void Every_shell_key_reaches_the_settings()
    {
        var config = Load("""
            [shell]
            theme = "Crux"
            button-layout = "close,minimize,maximize:menu"
            palette = "dark"
            font-size = 11.5
            background = "#023C88"
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
            mouse-button-modifier = "super"
            resize-with-right-button = false
            double-click-titlebar = "toggle_shade"
            middle-click-titlebar = "none"
            right-click-titlebar = "lower"
            tiling = false
            top-tiling = false

            [shell.keys]
            close = "Super+q"
            minimize = ""
            """);

        var shell = config.ShellSettings;
        Assert.Equal("Crux", shell.Theme);
        Assert.Equal("close,minimize,maximize:menu", shell.ButtonLayout);
        Assert.Equal("dark", shell.Palette);
        Assert.Equal(11.5, shell.FontSize);
        Assert.Equal("#023c88", shell.Background);
        Assert.Equal(6, shell.Workspaces);
        Assert.Equal(2, shell.WorkspaceRows);
        Assert.Equal(["Main", "Mail"], shell.WorkspaceNames);
        Assert.Equal(FocusMode.Sloppy, shell.FocusMode);
        Assert.Equal(FocusNewWindows.Strict, shell.FocusNewWindows);
        Assert.Equal(PlacementMode.Pointer, shell.Placement);
        Assert.False(shell.CenterNewWindows);
        Assert.False(shell.RaiseOnClick);
        Assert.True(shell.AutoRaise);
        Assert.Equal(250, shell.AutoRaiseDelay);
        Assert.Equal("Super", shell.MouseButtonModifier);
        Assert.False(shell.ResizeWithRightButton);
        Assert.Equal(TitlebarAction.ToggleShade, shell.DoubleClickTitlebar);
        Assert.Equal(TitlebarAction.None, shell.MiddleClickTitlebar);
        Assert.Equal(TitlebarAction.Lower, shell.RightClickTitlebar);
        Assert.False(shell.Tiling);
        Assert.False(shell.TopTiling);
        Assert.Equal([new ShellKey("close", "Super+q"), new ShellKey("minimize", "")], shell.Keys);
    }

    [Fact]
    public void A_file_without_the_tables_keeps_every_default()
    {
        var config = Load("compress = \"none\"");

        Assert.Equal(new ShellSettings(), config.ShellSettings with { WorkspaceNames = [], Keys = [] });
        Assert.Empty(config.ShellSettings.WorkspaceNames);
        Assert.Empty(config.ShellSettings.Keys);
        Assert.Equal(PanelSettings.DefaultSize, config.Panel.Size);
        Assert.Equal(PanelSettings.DefaultTop, config.Panel.Top);
        Assert.Equal(PanelSettings.DefaultBottom, config.Panel.Bottom);
    }

    [Fact]
    public void A_short_background_color_expands_to_six_digits()
    {
        Assert.Equal("#aabbcc", Load("[shell]\nbackground = \"#ABC\"").ShellSettings.Background);
    }

    [Fact]
    public void An_integer_font_size_reads_as_a_number()
    {
        Assert.Equal(14.0, Load("[shell]\nfont-size = 14").ShellSettings.FontSize);
    }

    [Theory]
    [InlineData("focus-mode = \"x\"", "focus-mode takes click, sloppy or mouse, ignoring 'x'")]
    [InlineData("focus-new-windows = \"loose\"", "focus-new-windows takes smart or strict, ignoring 'loose'")]
    [InlineData("placement = \"random\"", "placement takes automatic, pointer or manual, ignoring 'random'")]
    [InlineData("palette = \"sepia\"", "palette takes light or dark, ignoring 'sepia'")]
    [InlineData("double-click-titlebar = \"explode\"", "double-click-titlebar takes none, toggle_shade, toggle_maximize, toggle_maximize_horizontally, toggle_maximize_vertically, minimize, lower or menu, ignoring 'explode'")]
    [InlineData("middle-click-titlebar = 3", "middle-click-titlebar takes none, toggle_shade")]
    [InlineData("right-click-titlebar = \"\"", "right-click-titlebar takes none, toggle_shade")]
    [InlineData("mouse-button-modifier = \"Hyper\"", "mouse-button-modifier takes Alt, Super or Ctrl, ignoring 'Hyper'")]
    [InlineData("workspaces = 0", "workspaces takes 1 to 36, ignoring '0'")]
    [InlineData("workspaces = 37", "workspaces takes 1 to 36, ignoring '37'")]
    [InlineData("workspaces = \"four\"", "workspaces takes 1 to 36, ignoring 'four'")]
    [InlineData("workspace-rows = 5", "workspace-rows takes 1 to 4, ignoring '5'")]
    [InlineData("workspace-rows = 0", "workspace-rows takes 1 to 4, ignoring '0'")]
    [InlineData("font-size = 0", "font-size takes a positive number, ignoring '0'")]
    [InlineData("font-size = -2.5", "font-size takes a positive number, ignoring '-2.5'")]
    [InlineData("font-size = \"big\"", "font-size takes a positive number, ignoring 'big'")]
    [InlineData("background = \"blue\"", "background takes a color as #rrggbb or #rgb, ignoring 'blue'")]
    [InlineData("background = \"#12345\"", "background takes a color as #rrggbb or #rgb, ignoring '#12345'")]
    [InlineData("background = 7", "background takes a color as #rrggbb or #rgb, ignoring '7'")]
    [InlineData("auto-raise-delay = -1", "auto-raise-delay takes a whole number of at least 0, ignoring '-1'")]
    [InlineData("auto-raise-delay = 1.5", "auto-raise-delay takes a whole number of at least 0, ignoring '1.5'")]
    [InlineData("button-layout = \"menu:close,bogus\"", "button-layout takes menu, appmenu, minimize, maximize, close, shade, above, stick and spacer around one ':', ignoring 'menu:close,bogus'")]
    [InlineData("button-layout = \"a:b:c\"", "button-layout takes menu, appmenu")]
    [InlineData("theme = \"\"", "theme takes a name, ignoring ''")]
    [InlineData("theme = 7", "theme takes a name, ignoring '7'")]
    [InlineData("workspace-names = \"Main\"", "workspace-names takes an array of strings, ignoring it")]
    [InlineData("workspace-names = [1, 2]", "workspace-names takes an array of strings, ignoring it")]
    [InlineData("tiling = \"yes\"", "tiling takes true or false, ignoring 'yes'")]
    [InlineData("top-tiling = 1", "top-tiling takes true or false, ignoring '1'")]
    [InlineData("center-new-windows = \"no\"", "center-new-windows takes true or false, ignoring 'no'")]
    [InlineData("raise-on-click = \"no\"", "raise-on-click takes true or false, ignoring 'no'")]
    [InlineData("auto-raise = \"no\"", "auto-raise takes true or false, ignoring 'no'")]
    [InlineData("resize-with-right-button = \"no\"", "resize-with-right-button takes true or false, ignoring 'no'")]
    public void A_bad_value_warns_with_the_choices_and_keeps_the_default(string line, string warning)
    {
        var config = LoadCapturing($"[shell]\n{line}\n", out var warnings);

        Assert.Equal(new ShellSettings(), config.ShellSettings with { WorkspaceNames = [], Keys = [] });
        Assert.Empty(config.ShellSettings.WorkspaceNames);
        var line1 = Assert.Single(warnings, entry => entry.StartsWith("Warn", StringComparison.Ordinal));
        Assert.Contains("[shell] " + warning, line1, StringComparison.Ordinal);
    }

    [Fact]
    public void Workspace_rows_are_bounded_by_the_workspaces_the_same_file_sets()
    {
        var config = Load("[shell]\nworkspaces = 8\nworkspace-rows = 4\n");
        Assert.Equal(8, config.ShellSettings.Workspaces);
        Assert.Equal(4, config.ShellSettings.WorkspaceRows);

        Assert.Equal(1, Load("[shell]\nworkspaces = 1\nworkspace-rows = 2\n").ShellSettings.WorkspaceRows);
    }

    [Theory]
    [InlineData("Alt", "Alt")]
    [InlineData("ALT", "Alt")]
    [InlineData("super", "Super")]
    [InlineData("ctrl", "Ctrl")]
    public void The_mouse_button_modifier_is_stored_in_its_spelled_form(string text, string expected)
    {
        Assert.Equal(expected, Load($"[shell]\nmouse-button-modifier = \"{text}\"\n").ShellSettings.MouseButtonModifier);
    }

    [Fact]
    public void An_unknown_shell_key_name_warns_with_the_known_names_and_is_dropped()
    {
        var config = LoadCapturing("[shell.keys]\nexplode = \"Alt+F4\"\nclose = \"Alt+w\"\n", out var warnings);

        Assert.Equal([new ShellKey("close", "Alt+w")], config.ShellSettings.Keys);
        var warning = Assert.Single(warnings, entry => entry.StartsWith("Warn", StringComparison.Ordinal));
        Assert.Contains("[shell.keys] explode is not a key, ignoring it; the keys are switch-windows, ", warning, StringComparison.Ordinal);
        Assert.Contains("toggle-host-fullscreen", warning, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("close = \"Alt+Bogus\"", "[shell.keys] close: 'Alt+Bogus' names no key, ignoring it")]
    [InlineData("close = \"Hyper+F4\"", "[shell.keys] close: 'Hyper+F4' has no modifier named 'Hyper'; the modifiers are shift, ctrl, alt and super, ignoring it")]
    [InlineData("close = \"+\"", "[shell.keys] close: '+' names no key, ignoring it")]
    [InlineData("close = 4", "[shell.keys] close takes a chord such as Alt+F4, ignoring '4'")]
    public void A_shell_key_chord_that_does_not_parse_warns_and_is_dropped(string line, string warning)
    {
        var config = LoadCapturing($"[shell.keys]\n{line}\n", out var warnings);

        Assert.Empty(config.ShellSettings.Keys);
        var entry = Assert.Single(warnings, entry => entry.StartsWith("Warn", StringComparison.Ordinal));
        Assert.Contains(warning, entry, StringComparison.Ordinal);
    }

    [Fact]
    public void The_same_chord_on_two_shell_keys_loads_and_the_key_table_reports_it()
    {
        var config = LoadCapturing("[shell.keys]\nclose = \"Alt+F4\"\nminimize = \"Alt+F4\"\n", out var warnings);

        Assert.Equal(2, config.ShellSettings.Keys.Count);
        Assert.DoesNotContain(warnings, entry => entry.StartsWith("Warn", StringComparison.Ordinal));

        using var capture = new LogCapture();
        var table = KeyTable.Build(config.ShellSettings.Keys, []);
        Assert.Contains("Warn: shell key close and minimize both use Alt+F4, keeping close", capture.Lines);
        Assert.Equal("close", table.Match(ShellModifiers.Alt, 62));
        Assert.DoesNotContain(table.Bindings, binding => binding.Name == "minimize");
    }

    [Fact]
    public void Every_panel_key_reaches_the_settings()
    {
        var config = Load("""
            [panel]
            size = 32
            top = ["menu-bar", "spacer", "clock"]
            bottom = []
            """);

        Assert.Equal(32, config.Panel.Size);
        Assert.Equal(["menu-bar", "spacer", "clock"], config.Panel.Top);
        Assert.Empty(config.Panel.Bottom);
    }

    [Theory]
    [InlineData("size = 4", "[panel] size takes 8 to 128, ignoring '4'")]
    [InlineData("size = 129", "[panel] size takes 8 to 128, ignoring '129'")]
    [InlineData("size = \"tall\"", "[panel] size takes 8 to 128, ignoring 'tall'")]
    [InlineData("top = \"menu-bar\"", "[panel] top takes an array of applet names, ignoring it; the applets are menu-bar, window-list, workspace-switcher, clock, show-desktop, launcher:NAME and spacer")]
    [InlineData("bottom = [1]", "[panel] bottom takes an array of applet names, ignoring it")]
    public void A_bad_panel_value_warns_and_keeps_the_default(string line, string warning)
    {
        var config = LoadCapturing($"[panel]\n{line}\n", out var warnings);

        Assert.Equal(PanelSettings.DefaultSize, config.Panel.Size);
        Assert.Equal(PanelSettings.DefaultTop, config.Panel.Top);
        Assert.Equal(PanelSettings.DefaultBottom, config.Panel.Bottom);
        var entry = Assert.Single(warnings, entry => entry.StartsWith("Warn", StringComparison.Ordinal));
        Assert.Contains(warning, entry, StringComparison.Ordinal);
    }

    [Fact]
    public void The_shell_and_panel_tables_are_not_unknown_sections()
    {
        LoadCapturing("[shell]\ntiling = false\n\n[shell.keys]\nclose = \"Alt+w\"\n\n[panel]\nsize = 30\n\n[bogus]\nx = 1\n", out var warnings);

        var warning = Assert.Single(warnings, entry => entry.StartsWith("Warn", StringComparison.Ordinal));
        Assert.Contains("unknown section '[bogus]'", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void The_placeholder_documents_the_three_tables_and_loads_as_defaults()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "waylonia.toml");
        Config.WritePlaceholder(path, BasinLogger.None);
        var text = File.ReadAllText(path);

        Assert.Contains("#[shell]\n", text, StringComparison.Ordinal);
        Assert.Contains("#[shell.keys]\n", text, StringComparison.Ordinal);
        Assert.Contains("#[panel]\n", text, StringComparison.Ordinal);
        Assert.Contains("#bottom = [\"window-list\", \"workspace-switcher\"]\n", text, StringComparison.Ordinal);
        var config = Config.Load(WayloniaPaths.ConfigOnly(path), BasinLogger.None);
        Assert.Equal(new ShellSettings(), config.ShellSettings with { WorkspaceNames = [], Keys = [] });
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
