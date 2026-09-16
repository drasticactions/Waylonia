using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Basin.Diagnostics;
using Waylonia.Ui;
using Xunit;

namespace Waylonia.Tests;

public sealed class SettingsWindowTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "waylonia-settings-" + Guid.NewGuid().ToString("n"));

    private string _path = string.Empty;

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
    public void Deleting_a_desktop_selects_its_neighbour_and_revert_reloads_the_file()
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
    public void The_window_shows_four_tabs_and_binds_the_form_and_the_desktop_list()
    {
        var model = Model("[host]\ncapture-chord = \"RightAlt\"\n\n[desktops.lab]\nrecipe = \"sway\"\n");
        var window = new SettingsWindow(model);
        window.Show();

        var tabs = window.FindControl<TabControl>("Tabs")!;
        Assert.Equal(["Host", "Defaults", "Hotkeys", "Desktops"], tabs.Items.OfType<TabItem>().Select(tab => tab.Header?.ToString()));
        Assert.Equal("RightAlt", window.FindControl<TextBox>("CaptureChordBox")!.Text);
        var list = window.FindControl<ListBox>("DesktopList")!;
        Assert.Same(model.SelectedDesktop, list.SelectedItem);
        Assert.Same(model.SaveSettingsCommand, window.FindControl<Button>("SaveButton")!.Command);

        model.Form.CaptureChord = "RightControl";
        Assert.Equal("RightControl", window.FindControl<TextBox>("CaptureChordBox")!.Text);
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
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
