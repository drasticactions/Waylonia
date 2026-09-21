using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Basin.Diagnostics;
using Basin.Shell.Nested;
using Waylonia.Shell;
using Waylonia.UI;
using Xunit;

namespace Waylonia.Tests;

public sealed class SettingsWindowTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "waylonia-settings-" + Guid.NewGuid().ToString("n"));

    private readonly string? _dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");

    private readonly string? _dataDirectories = Environment.GetEnvironmentVariable("XDG_DATA_DIRS");

    private string _path = string.Empty;

    private void InstallTheme(string name)
    {
        var directory = Path.Combine(_directory, "themes", name, "metacity-1");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "metacity-theme-3.xml"), "<metacity_theme />");
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", _directory);
        Environment.SetEnvironmentVariable("XDG_DATA_DIRS", Path.Combine(_directory, "empty"));
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
        Basin.Shell.Nested.PlacementMode.Pointer,
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

    private SettingsViewModel Model(string? text = null, ConfigValues? running = null, List<Config>? applied = null)
    {
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, "waylonia.toml");
        if (text is not null)
        {
            File.WriteAllText(_path, text);
        }

        return new SettingsViewModel(_path, running ?? new ConfigValues(), BasinLogger.None, applied is null ? null : applied.Add);
    }

    [Fact]
    public void Loading_shows_what_the_file_says()
    {
        var model = Model("""
            compress = "zstd"
            audio = true
            lang = ""
            terminal = "foot -e"

            [host]
            clipboard = false
            capture-chord = "RightAlt"

            [hotkeys]
            "ctrl+alt+t" = "foot"

            [desktops.lab]
            recipe = "sway"
            host = "devbox"
            env = ["A=1", "B=2"]
            """);

        Assert.Equal(_path, model.StatusText);
        Assert.True(model.CanSave);
        Assert.Null(model.Problem);
        Assert.Equal(2, model.Form.CompressIndex);
        Assert.True(model.Form.Audio);
        Assert.Null(model.Form.Gpu);
        Assert.Equal("\"\"", model.Form.Lang);
        Assert.Equal("foot -e", model.Form.Terminal);
        Assert.False(model.Form.Clipboard);
        Assert.True(model.Form.Drag);
        Assert.Equal("RightAlt", model.Form.CaptureChord);
        Assert.Equal("ctrl+alt+t = foot", model.Form.Hotkeys);
        var desktop = Assert.Single(model.Desktops);
        Assert.Same(desktop, model.SelectedDesktop);
        Assert.Equal("lab", desktop.Name);
        Assert.Equal(1, desktop.RecipeIndex);
        Assert.Equal("devbox", desktop.Host);
        Assert.Equal("A=1\nB=2", desktop.Env);
    }

    [Fact]
    public void A_missing_file_loads_defaults_and_says_save_creates_it()
    {
        var model = Model();
        Assert.Equal(_path, model.StatusText);
        Assert.True(model.CanSave);
        Assert.Equal(string.Empty, model.Form.CaptureChord);
        Assert.Equal(0, model.Form.CompressIndex);
        Assert.Empty(model.Desktops);
        Assert.Null(model.SelectedDesktop);
    }

    [Fact]
    public void Saving_writes_the_changed_fields_applies_them_and_names_what_needs_a_restart()
    {
        var applied = new List<Config>();
        var model = Model("# mine\n", applied: applied);
        model.Form.Clipboard = false;
        model.Form.SessionTitles = false;
        model.Form.SshTimeout = "10";
        model.Form.CompressIndex = 3;
        model.Form.Hotkeys = "ctrl+alt+t = foot";
        model.NewDesktop();
        model.SelectedDesktop!.Name = "lab";
        model.SelectedDesktop.RecipeIndex = 1;
        model.SelectedDesktop.Size = "1920x1080";

        Assert.True(model.Save());

        Assert.Equal("""
            # mine

            compress = "none"

            [host]
            clipboard = false
            session-titles = false
            ssh-timeout = 10

            [hotkeys]
            "ctrl+alt+t" = "foot"

            [desktops.lab]
            recipe = "sway"
            size = "1920x1080"

            """.ReplaceLineEndings("\n"), File.ReadAllText(_path));
        Assert.Equal("Saved. clipboard takes effect when Waylonia restarts.", model.Note);
        Assert.Null(model.Problem);
        var config = Assert.Single(applied);
        Assert.False(config.Host.Clipboard);
        Assert.False(config.Host.SessionTitles);
        Assert.Equal(10, config.Host.SshTimeout);
        Assert.Equal("none", config.Compress);
        Assert.Equal(_path, model.StatusText);
    }

    [Fact]
    public void A_second_save_compares_with_the_running_values_not_the_last_save()
    {
        var model = Model(running: new ConfigValues(Tray: false));
        Assert.Equal("Saved. tray takes effect when Waylonia restarts.", model.Note?.Replace("Saved. ", "Saved. ", StringComparison.Ordinal));

        model.Form.Tray = false;
        Assert.True(model.Save());
        Assert.Equal("Saved.", model.Note);

        model.Form.Tray = true;
        model.Form.Drag = false;
        Assert.True(model.Save());
        Assert.Equal("Saved. tray and drag take effect when Waylonia restarts.", model.Note);
    }

    [Fact]
    public void Fields_with_problems_block_the_save_and_say_why()
    {
        var model = Model();
        model.Form.CaptureChord = "nope";
        model.Form.Video = "mpeg";
        model.Form.Lang = "en US";
        model.Form.Socket = "/run/wayland-9";
        model.Form.Hotkeys = "ctrl+alt+t";

        Assert.False(model.Save());
        Assert.Equal("Some fields have a problem. See the messages under them.", model.Problem);
        Assert.Equal("Use one modifier key, such as RightControl, or double:RightControl for a double tap.", model.Form.CaptureChordProblem);
        Assert.Equal("Use none, h264, vp9 or av1. Add ,hw to decode on this host's GPU.", model.Form.VideoProblem);
        Assert.Equal("Use a locale name, such as en_US.UTF-8. Write \"\" to leave the remote alone.", model.Form.LangProblem);
        Assert.Equal("Use a socket name, such as wayland-9, not a path.", model.Form.SocketProblem);
        Assert.Equal("Write one hotkey per line as CHORD = COMMAND. 'ctrl+alt+t' has no '='.", model.Form.HotkeysProblem);
        Assert.False(File.Exists(_path));
    }

    [Fact]
    public void A_broken_desktop_is_selected_and_its_field_says_what_is_wrong()
    {
        var model = Model();
        model.NewDesktop();
        var first = model.SelectedDesktop!;
        first.Name = "lab";
        first.RecipeIndex = 1;
        model.NewDesktop();
        var second = model.SelectedDesktop!;
        model.SelectedDesktop = first;

        Assert.False(model.Save());
        Assert.Same(second, model.SelectedDesktop);
        Assert.Equal("Give the desktop a name.", second.NameProblem);
        Assert.Equal("The desktop new desktop has a problem. See its fields.", model.Problem);

        second.Name = "lab";
        second.RecipeIndex = 1;
        model.SelectedDesktop = first;
        Assert.False(model.Save());
        Assert.Same(second, model.SelectedDesktop);
        Assert.Equal("Two desktops are named lab. Use another name.", second.NameProblem);
        Assert.Equal("The desktop lab has a problem. See its fields.", model.Problem);

        second.Name = "mine";
        second.RecipeIndex = 0;
        Assert.False(model.Save());
        Assert.Equal("'mine' is not a recipe. Pick sway, niri, plasma, cosmic, xfce, or custom with a command.", second.RecipeProblem);

        second.RecipeIndex = DesktopFormViewModel.RecipeChoices.Count - 1;
        Assert.False(model.Save());
        Assert.Equal("A custom desktop needs the command that starts it.", second.CommandProblem);

        second.Command = "cage -- xterm";
        second.Size = "big";
        Assert.False(model.Save());
        Assert.Equal("Use WIDTHxHEIGHT, such as 1920x1080.", second.SizeProblem);

        second.Size = string.Empty;
        Assert.True(model.Save());
        Assert.Contains("[desktops.mine]\nrecipe = \"custom\"\ncommand = \"cage -- xterm\"\n", File.ReadAllText(_path), StringComparison.Ordinal);
    }

    [Fact]
    public void A_broken_file_disables_save_and_names_the_error()
    {
        var model = Model("compress = \n");

        Assert.False(model.CanSave);
        Assert.False(model.SaveSettingsCommand.CanExecute(null));
        Assert.StartsWith($"{_path} did not parse:", model.Problem, StringComparison.Ordinal);
        Assert.EndsWith("Fix the file by hand; nothing is saved until it loads.", model.Problem, StringComparison.Ordinal);
        Assert.Equal(0, model.Form.CompressIndex);
    }

    [Fact]
    public void Deleting_a_desktop_selects_its_neighbor_and_revert_reloads_the_file()
    {
        var model = Model("[desktops.a]\nrecipe = \"sway\"\n\n[desktops.b]\nrecipe = \"niri\"\n\n[desktops.c]\nrecipe = \"xfce\"\n");
        Assert.Equal(["a", "b", "c"], model.Desktops.Select(desktop => desktop.Name));
        Assert.True(model.DeleteDesktopCommand.CanExecute(null));

        model.SelectedDesktop = model.Desktops[1];
        model.DeleteDesktop();
        Assert.Equal(["a", "c"], model.Desktops.Select(desktop => desktop.Name));
        Assert.Equal("c", model.SelectedDesktop!.Name);

        model.DeleteDesktop();
        model.DeleteDesktop();
        Assert.Empty(model.Desktops);
        Assert.Null(model.SelectedDesktop);
        Assert.False(model.DeleteDesktopCommand.CanExecute(null));

        model.Form.Tray = false;
        model.Reload();
        Assert.True(model.Form.Tray);
        Assert.Equal(["a", "b", "c"], model.Desktops.Select(desktop => desktop.Name));
    }

    [Fact]
    public void A_skipped_config_file_has_nothing_to_edit()
    {
        var model = new SettingsViewModel(null, new ConfigValues(), BasinLogger.None);
        Assert.False(model.CanSave);
        Assert.Equal("This run skips the config file (--config false), so there is nothing to edit.", model.Problem);
        Assert.False(model.Save());
    }

    [Fact]
    public void Problems_raise_change_notifications_so_the_view_updates()
    {
        var model = Model();
        var changed = new List<string>();
        model.Form.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);
        model.Form.Video = "mpeg";
        model.Save();

        Assert.Contains(nameof(SettingsFormViewModel.VideoProblem), changed);
        Assert.Contains(nameof(SettingsFormViewModel.Problems), changed);
    }

    [AvaloniaFact]
    public void The_window_shows_five_tabs_and_binds_the_form_and_the_desktop_list()
    {
        var model = Model("[host]\ncapture-chord = \"RightAlt\"\n\n[desktops.lab]\nrecipe = \"sway\"\n");
        var window = new SettingsWindow(model);
        window.Show();

        var tabs = window.View.FindControl<TabControl>("Tabs")!;
        Assert.Equal(["Host", "Defaults", "Hotkeys", "Desktops", "Shell"], tabs.Items.OfType<TabItem>().Select(tab => tab.Header?.ToString()));
        Assert.Equal("RightAlt", window.View.FindControl<TextBox>("CaptureChordBox")!.Text);
        var list = window.View.FindControl<ListBox>("DesktopList")!;
        Assert.Same(model.SelectedDesktop, list.SelectedItem);
        Assert.Same(model.SaveSettingsCommand, window.View.FindControl<Button>("SaveButton")!.Command);

        model.Form.CaptureChord = "RightControl";
        Assert.Equal("RightControl", window.View.FindControl<TextBox>("CaptureChordBox")!.Text);
    }

    [Fact]
    public void The_shell_tab_loads_defaults_as_blank_fields_and_the_built_in_choices()
    {
        var model = Model();
        var form = model.Form;
        Assert.Equal(0, form.ShellModeIndex);
        Assert.Equal(["windows", "nested"], SettingsFormViewModel.ShellModeChoices);
        Assert.Equal(ShellSettings.DefaultTheme, form.ThemeChoices[0]);
        Assert.Equal(string.Empty, form.Theme);
        Assert.Equal(string.Empty, form.ButtonLayout);
        Assert.Equal(0, form.PaletteIndex);
        Assert.Equal(string.Empty, form.FontSize);
        Assert.Equal(string.Empty, form.Background);
        Assert.Equal(string.Empty, form.Workspaces);
        Assert.Equal(string.Empty, form.WorkspaceRows);
        Assert.Equal(string.Empty, form.WorkspaceNames);
        Assert.Equal(string.Empty, form.AutoRaiseDelay);
        Assert.Equal(string.Empty, form.ShellKeys);
        Assert.Equal(string.Empty, form.PanelSize);
        Assert.Equal("menu-bar", form.PanelTop);
        Assert.Equal("window-list, workspace-switcher", form.PanelBottom);
        Assert.Equal(
            ["none", "toggle_shade", "toggle_maximize", "toggle_maximize_horizontally", "toggle_maximize_vertically", "minimize", "lower", "menu"],
            SettingsFormViewModel.TitlebarActionChoices);
        Assert.Equal(2, form.DoubleClickTitlebarIndex);
        Assert.Equal(6, form.MiddleClickTitlebarIndex);
        Assert.Equal(7, form.RightClickTitlebarIndex);

        var values = form.Validate();
        Assert.NotNull(values);
        Assert.Equal(ShellMode.Windows, values.Shell);
        var defaults = new ShellSettings();
        Assert.Equal(defaults, values.ShellSettings with { WorkspaceNames = defaults.WorkspaceNames, Keys = defaults.Keys });
        Assert.Empty(values.ShellSettings.WorkspaceNames);
        Assert.Empty(values.ShellSettings.Keys);
        Assert.Equal(PanelSettings.DefaultSize, values.Panel.Size);
        Assert.Equal(PanelSettings.DefaultTop, values.Panel.Top);
        Assert.Equal(PanelSettings.DefaultBottom, values.Panel.Bottom);
    }

    [Fact]
    public void The_shell_tab_loads_every_value_and_validates_it_back()
    {
        InstallTheme("Crux");
        var shell = EveryShellValue();
        var panel = new PanelSettings(32, ["menu-bar", "spacer", "clock"], []);
        var form = new SettingsFormViewModel();

        form.Load(new ConfigValues(Shell: ShellMode.Nested, ShellSettings: shell, Panel: panel));

        Assert.Equal(1, form.ShellModeIndex);
        Assert.Equal(["Atlanta", "Crux"], form.ThemeChoices);
        Assert.Equal("Crux", form.Theme);
        Assert.Equal("close,minimize,maximize:menu", form.ButtonLayout);
        Assert.Equal(1, form.PaletteIndex);
        Assert.Equal("11.5", form.FontSize);
        Assert.Equal("#023c88", form.Background);
        Assert.Equal("6", form.Workspaces);
        Assert.Equal("2", form.WorkspaceRows);
        Assert.Equal("Main, Mail", form.WorkspaceNames);
        Assert.Equal(1, form.FocusModeIndex);
        Assert.Equal(1, form.FocusNewWindowsIndex);
        Assert.Equal(1, form.PlacementIndex);
        Assert.False(form.CenterNewWindows);
        Assert.False(form.RaiseOnClick);
        Assert.True(form.AutoRaise);
        Assert.Equal("250", form.AutoRaiseDelay);
        Assert.Equal(1, form.MouseButtonModifierIndex);
        Assert.False(form.ResizeWithRightButton);
        Assert.Equal(1, form.DoubleClickTitlebarIndex);
        Assert.Equal(0, form.MiddleClickTitlebarIndex);
        Assert.Equal(6, form.RightClickTitlebarIndex);
        Assert.False(form.Tiling);
        Assert.False(form.TopTiling);
        Assert.Equal("close = Super+q\nminimize = \ntile-to-side-w = Super+Left", form.ShellKeys);
        Assert.Equal("32", form.PanelSize);
        Assert.Equal("menu-bar, spacer, clock", form.PanelTop);
        Assert.Equal(string.Empty, form.PanelBottom);

        var values = form.Validate();
        Assert.NotNull(values);
        Assert.Equal(ShellMode.Nested, values.Shell);
        Assert.Equal(shell, values.ShellSettings with { WorkspaceNames = shell.WorkspaceNames, Keys = shell.Keys });
        Assert.Equal(shell.WorkspaceNames, values.ShellSettings.WorkspaceNames);
        Assert.Equal(shell.Keys, values.ShellSettings.Keys);
        Assert.Equal(32, values.Panel.Size);
        Assert.Equal(["menu-bar", "spacer", "clock"], values.Panel.Top);
        Assert.Empty(values.Panel.Bottom);
    }

    [Fact]
    public void Shell_fields_with_problems_say_why()
    {
        InstallTheme("Crux");
        var model = Model();
        var form = model.Form;
        form.Theme = "Nope";
        form.ButtonLayout = "menu:close:minimize";
        form.FontSize = "big";
        form.Background = "blue";
        form.Workspaces = "40";
        form.WorkspaceRows = "0";
        form.AutoRaiseDelay = "-1";
        form.ShellKeys = "close = Hyper+F4";
        form.PanelSize = "4";
        form.PanelTop = "menu-bar, launcher:";
        form.PanelBottom = "clock, weather";

        Assert.False(model.Save());
        Assert.Equal("Some fields have a problem. See the messages under them.", model.Problem);
        Assert.Equal("Use a theme name; the bundled theme is Atlanta and the installed ones are Crux.", form.ThemeProblem);
        Assert.Equal($"Use {ShellConfig.ButtonNames} around one ':', such as menu:minimize,maximize,close.", form.ButtonLayoutProblem);
        Assert.Equal("Use a positive number, such as 13.", form.FontSizeProblem);
        Assert.Equal("Use a color as #rrggbb or #rgb, such as #5891ad.", form.BackgroundProblem);
        Assert.Equal("Use 1 to 36.", form.WorkspacesProblem);
        Assert.Equal("Use 1 to 4, the number of workspaces.", form.WorkspaceRowsProblem);
        Assert.Equal("Use a whole number of milliseconds, 0 or more.", form.AutoRaiseDelayProblem);
        Assert.Equal("close: 'Hyper+F4' has no modifier named 'Hyper'; the modifiers are shift, ctrl, alt and super.", form.ShellKeysProblem);
        Assert.Equal("Use 8 to 128, in pixels.", form.PanelSizeProblem);
        Assert.Equal($"'launcher:' is not an applet. The applets are {PanelApplets.AppletNames}, separated by commas.", form.PanelTopProblem);
        Assert.Equal($"'weather' is not an applet. The applets are {PanelApplets.AppletNames}, separated by commas.", form.PanelBottomProblem);
        Assert.False(File.Exists(_path));
    }

    [Fact]
    public void A_bad_theme_with_nothing_installed_names_the_bundled_one()
    {
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", Path.Combine(_directory, "empty"));
        Environment.SetEnvironmentVariable("XDG_DATA_DIRS", Path.Combine(_directory, "empty"));
        var form = new SettingsFormViewModel();
        form.Load(new ConfigValues());
        form.Theme = "Nope";

        Assert.Null(form.Validate());
        Assert.Equal("Use a theme name; the bundled theme is Atlanta and no other theme is installed.", form.ThemeProblem);

        form.Theme = "Atlanta";
        Assert.NotNull(form.Validate());
        Assert.Null(form.ThemeProblem);
    }

    [Fact]
    public void Workspace_rows_are_checked_against_the_workspace_count()
    {
        var form = new SettingsFormViewModel();
        form.Load(new ConfigValues());
        form.Workspaces = "1";
        form.WorkspaceRows = "2";

        Assert.Null(form.Validate());
        Assert.Equal("Use 1; there is one workspace.", form.WorkspaceRowsProblem);

        form.Workspaces = "6";
        form.WorkspaceRows = "3";
        var values = form.Validate();
        Assert.NotNull(values);
        Assert.Equal(6, values.ShellSettings.Workspaces);
        Assert.Equal(3, values.ShellSettings.WorkspaceRows);
    }

    [Fact]
    public void Saving_the_shell_tab_writes_the_tables_and_reads_them_back()
    {
        InstallTheme("Crux");
        var model = Model();
        var form = model.Form;
        form.ShellModeIndex = 1;
        form.Theme = "Crux";
        form.PaletteIndex = 1;
        form.FontSize = "11.5";
        form.Background = "#ABC";
        form.Workspaces = "6";
        form.WorkspaceNames = "Main, Mail";
        form.FocusModeIndex = 1;
        form.AutoRaise = true;
        form.ShellKeys = "close = Super+q\nminimize =";
        form.PanelSize = "32";
        form.PanelBottom = string.Empty;

        Assert.True(model.Save());
        Assert.Equal("Saved. shell takes effect when Waylonia restarts.", model.Note);

        var config = Config.Load(WayloniaPaths.ConfigOnly(_path), BasinLogger.None);
        Assert.Equal(ShellMode.Nested, config.Shell);
        Assert.Equal("Crux", config.ShellSettings.Theme);
        Assert.Equal("dark", config.ShellSettings.Palette);
        Assert.Equal(11.5, config.ShellSettings.FontSize);
        Assert.Equal("#aabbcc", config.ShellSettings.Background);
        Assert.Equal(6, config.ShellSettings.Workspaces);
        Assert.Equal(["Main", "Mail"], config.ShellSettings.WorkspaceNames);
        Assert.Equal(FocusMode.Sloppy, config.ShellSettings.FocusMode);
        Assert.True(config.ShellSettings.AutoRaise);
        Assert.Equal([new ShellKey("close", "Super+q"), new ShellKey("minimize", string.Empty)], config.ShellSettings.Keys);
        Assert.Equal(32, config.Panel.Size);
        Assert.Equal(PanelSettings.DefaultTop, config.Panel.Top);
        Assert.Empty(config.Panel.Bottom);
        var text = File.ReadAllText(_path);
        Assert.Contains("[shell]\ntheme = \"Crux\"\n", text, StringComparison.Ordinal);
        Assert.Contains("[panel]\nsize = 32\nbottom = []\n", text, StringComparison.Ordinal);

        model.Reload();
        Assert.Equal(1, form.ShellModeIndex);
        Assert.Equal("Crux", form.Theme);
        Assert.Equal("close = Super+q\nminimize = ", form.ShellKeys);
    }

    [AvaloniaFact]
    public void The_shell_tab_binds_the_mode_and_theme_choices()
    {
        InstallTheme("Crux");
        var model = Model("[host]\nshell = \"nested\"\n\n[shell]\ntheme = \"Crux\"\n");
        var window = new SettingsWindow(model);
        window.Show();
        var tabs = window.View.FindControl<TabControl>("Tabs")!;
        tabs.SelectedIndex = 4;
        window.UpdateLayout();

        var mode = window.View.FindControl<ComboBox>("ShellModeBox")!;
        Assert.Equal(["windows", "nested"], mode.Items.OfType<string>());
        Assert.Equal(1, mode.SelectedIndex);
        var theme = window.View.FindControl<ComboBox>("ThemeBox")!;
        Assert.Equal(["Atlanta", "Crux"], theme.Items.OfType<string>());
        Assert.Equal("Crux", theme.Text);
        Assert.Equal(string.Empty, window.View.FindControl<TextBox>("ShellKeysBox")!.Text ?? string.Empty);

        model.Form.ShellKeys = "close = Alt+F4";
        Assert.Equal("close = Alt+F4", window.View.FindControl<TextBox>("ShellKeysBox")!.Text);
    }

    [AvaloniaFact]
    public void Closing_the_window_hides_it_until_the_app_allows_the_close()
    {
        var window = new SettingsWindow(Model());
        window.Show();

        window.Close();
        Assert.False(window.IsVisible);

        window.Show();
        window.AllowClose();
        window.Close();
        Assert.False(window.IsVisible);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", _dataHome);
        Environment.SetEnvironmentVariable("XDG_DATA_DIRS", _dataDirectories);
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
