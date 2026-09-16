using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Basin.Diagnostics;
using Waylonia.Sessions;
using Waylonia.Ui;
using Xunit;

namespace Waylonia.Tests;

public sealed class ManagerWindowTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "waylonia-manager-" + Guid.NewGuid().ToString("n"));

    private ManagerViewModel Model(List<string>? calls = null)
    {
        calls ??= [];
        return new ManagerViewModel(
            new SessionStore(_directory),
            new SessionRegistry(new StubSessionHost()),
            BasinLogger.None,
            profile =>
            {
                calls.Add($"connect {profile.Name}");
                return Task.FromResult<string?>(null);
            },
            name =>
            {
                calls.Add($"disconnect {name}");
                return Task.CompletedTask;
            });
    }

    [AvaloniaFact]
    public void A_new_session_is_saved_as_a_file_with_the_edited_fields()
    {
        var model = Model();
        var window = new ManagerWindow(model);
        window.Show();
        model.NewSession();
        model.Form.Name = "dev";
        model.Form.Ssh = "user@devbox";
        model.Form.Command = "tmux new -A -s main";
        model.Form.Autostart = "foot\nfirefox";
        model.Form.CompressIndex = 3;
        model.Form.Gpu = true;
        model.Form.Audio = false;
        model.Form.Autoconnect = true;
        model.Form.Hotkeys = "ctrl+alt+t = foot";

        model.SaveSessionCommand.Execute(null);

        var text = File.ReadAllText(Path.Combine(_directory, "dev.toml"));
        Assert.Equal("""
            ssh = "user@devbox"
            command = "tmux new -A -s main"
            autostart = ["foot", "firefox"]
            compress = "none"
            gpu = true
            audio = false
            autoconnect = true

            [hotkeys]
            "ctrl+alt+t" = "foot"

            """.ReplaceLineEndings("\n"), text);
        Assert.Equal("dev", model.Selected);
        Assert.Contains(model.Rows, row => row.Name == "dev");
        Assert.Equal("dev", window.FindControl<TextBox>("NameBox")!.Text);
    }

    [Fact]
    public void Each_problem_is_reported_in_plain_words_under_its_own_field()
    {
        var model = Model();
        model.NewSession();
        Assert.False(model.Save());
        Assert.Equal("Give the session a name.", model.Form.NameProblem);
        Assert.Equal("Give the ssh destination, such as user@host.", model.Form.SshProblem);

        var form = model.Form;
        form.Name = "user@host";
        form.Ssh = "user@host";
        form.Video = "mpeg";
        form.Lang = "C.UTF-8; rm -rf /";
        form.DesktopIndex = SessionFormViewModel.DesktopChoices.ToList().IndexOf("custom");
        form.DesktopSize = "wide";
        form.Hotkeys = "hyper+t = foot";
        Assert.False(model.Save());

        Assert.Equal("Use only letters, digits, '.', '_' and '-' in the name.", form.NameProblem);
        Assert.Null(form.SshProblem);
        Assert.Equal("Use none, h264, vp9 or av1. Add ,hw to decode on this host's GPU.", form.VideoProblem);
        Assert.Equal("Use a locale name, such as en_US.UTF-8. Write \"\" to leave the remote alone.", form.LangProblem);
        Assert.Equal("A custom desktop needs the command that starts it.", form.CommandProblem);
        Assert.Equal("Use WIDTHxHEIGHT, such as 1920x1080.", form.DesktopSizeProblem);
        Assert.Equal("'hyper+t' is not a chord. Use modifiers and one key, such as ctrl+alt+t.", form.HotkeysProblem);
        Assert.Equal(6, form.Problems.Count);
        Assert.Null(model.Problem);

        form.Name = "dev";
        form.Video = "h264,hw";
        form.Lang = "en_US.UTF-8";
        form.Command = "my-session";
        form.DesktopSize = "1920x1080";
        form.Hotkeys = "ctrl+alt+t";
        Assert.False(model.Save());
        Assert.Equal("Write one hotkey per line as CHORD = COMMAND. 'ctrl+alt+t' has no '='.", form.HotkeysProblem);

        form.Hotkeys = string.Empty;
        Assert.True(model.Save());
        Assert.Empty(form.Problems);
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Fact]
    public void A_name_that_is_taken_is_reported_under_the_name_field()
    {
        new SessionStore(_directory).Save(new SessionProfile("dev", "user@devbox"));
        var model = Model();
        model.NewSession();
        model.Form.Name = "dev";
        model.Form.Ssh = "user@other";

        Assert.False(model.Save());
        Assert.Equal("A session named dev exists. Use another name.", model.Form.NameProblem);
    }

    [Fact]
    public void Problems_raise_change_notifications_so_the_view_updates()
    {
        var model = Model();
        var changed = new List<string>();
        model.Form.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);
        model.NewSession();
        changed.Clear();

        model.Save();

        Assert.Contains(nameof(SessionFormViewModel.NameProblem), changed);
        Assert.Contains(nameof(SessionFormViewModel.SshProblem), changed);
    }

    [AvaloniaFact]
    public void The_menu_keeps_new_and_settings_while_the_toolbar_and_row_menu_carry_the_rest()
    {
        var window = new ManagerWindow(Model());
        window.Show();

        var menu = window.FindControl<Menu>("MenuBar")!;
        var top = menu.Items.OfType<MenuItem>().ToList();
        Assert.Equal(2, top.Count);
        var session = top[0];
        Assert.Equal("Session", session.Header);
        var item = Assert.IsType<MenuItem>(Assert.Single(session.Items));
        Assert.Equal("New", item.Header);
        Assert.Same(window.Model.NewSessionCommand, item.Command);
        Assert.Equal("Settings", top[1].Header);
        Assert.Same(window.Model.OpenSettingsCommand, top[1].Command);
        Assert.False(window.Model.OpenSettingsCommand.CanExecute(null));

        Assert.Same(window.Model.NewSessionCommand, window.FindControl<Button>("NewButton")!.Command);
        Assert.Same(window.Model.SaveSessionCommand, window.FindControl<Button>("SaveButton")!.Command);
        Assert.Same(window.Model.DeleteSessionCommand, window.FindControl<Button>("DeleteButton")!.Command);
        var connect = window.FindControl<Button>("ConnectButton")!;
        Assert.Same(window.Model.ToggleConnectionCommand, connect.Command);
        Assert.Equal("Connect", connect.Content);

        var rowItems = window.FindControl<ContextMenu>("RowMenu")!.Items.OfType<MenuItem>().ToList();
        Assert.Equal(["Save", "Delete"], rowItems.Skip(1).Select(entry => entry.Header?.ToString()));
        Assert.Equal(3, rowItems.Count);
    }

    [AvaloniaFact]
    public void A_right_click_selects_the_row_and_opens_its_menu_but_not_on_empty_space()
    {
        new SessionStore(_directory).Save(new SessionProfile("dev", "user@devbox"));
        new SessionStore(_directory).Save(new SessionProfile("lab", "user@lab"));
        var window = new ManagerWindow(Model());
        window.Show();
        var list = window.FindControl<ListBox>("SessionList")!;
        var rowMenu = window.FindControl<ContextMenu>("RowMenu")!;
        list.UpdateLayout();

        var row = (ListBoxItem)list.ContainerFromIndex(1)!;
        var inside = row.TranslatePoint(new Point(4, 4), window)!.Value;
        window.MouseDown(inside, MouseButton.Right);
        window.MouseUp(inside, MouseButton.Right);
        Assert.Equal("lab", window.Model.Selected);
        Assert.True(rowMenu.IsOpen);
        var rowItems = rowMenu.Items.OfType<MenuItem>().ToList();
        Assert.Equal("Connect", rowItems[0].Header);
        Assert.Equal(
            [window.Model.ToggleConnectionCommand, window.Model.SaveSessionCommand, window.Model.DeleteSessionCommand],
            rowItems.Select(entry => entry.Command));

        rowMenu.Close();
        var below = list.TranslatePoint(new Point(4, list.Bounds.Height - 4), window)!.Value;
        window.MouseDown(below, MouseButton.Right);
        window.MouseUp(below, MouseButton.Right);
        Assert.False(rowMenu.IsOpen);
        Assert.Equal("lab", window.Model.Selected);
    }

    [Fact]
    public void The_settings_command_calls_through_when_the_app_offers_it()
    {
        var opened = 0;
        var model = new ManagerViewModel(
            new SessionStore(_directory),
            new SessionRegistry(new StubSessionHost()),
            BasinLogger.None,
            _ => Task.FromResult<string?>(null),
            _ => Task.CompletedTask,
            () => opened++);

        Assert.True(model.OpenSettingsCommand.CanExecute(null));
        model.OpenSettingsCommand.Execute(null);
        Assert.Equal(1, opened);
    }

    [Fact]
    public void Editing_an_existing_session_rewrites_its_file_and_a_rename_moves_it()
    {
        new SessionStore(_directory).Save(new SessionProfile("dev", "user@devbox", Terminal: "foot -e"));
        var model = Model();
        model.Select("dev");
        Assert.Equal("user@devbox", model.Form.Ssh);
        Assert.Equal("foot -e", model.Form.Terminal);

        model.Form.Ssh = "user@newbox";
        Assert.True(model.Save());
        Assert.Contains("ssh = \"user@newbox\"", File.ReadAllText(Path.Combine(_directory, "dev.toml")), StringComparison.Ordinal);

        model.Form.Name = "lab";
        Assert.True(model.Save());
        Assert.False(File.Exists(Path.Combine(_directory, "dev.toml")));
        Assert.True(File.Exists(Path.Combine(_directory, "lab.toml")));
    }

    [Fact]
    public void Deleting_removes_the_file_and_a_broken_file_is_listed_with_its_error()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "bad.toml"), "ssh = \"oops");
        new SessionStore(_directory).Save(new SessionProfile("dev", "user@devbox"));
        var model = Model();

        Assert.Contains(model.Rows, row => row.Name == "bad" && row.Broken is not null && row.Detail.StartsWith("broken:", StringComparison.Ordinal));
        model.Select("bad");
        Assert.StartsWith("This file does not load:", model.Problem, StringComparison.Ordinal);
        Assert.False(model.CanConnect);

        model.Select("dev");
        Assert.True(model.CanDelete);
        model.DeleteSessionCommand.Execute(null);

        Assert.False(File.Exists(Path.Combine(_directory, "dev.toml")));
        Assert.DoesNotContain(model.Rows, row => row.Name == "dev");
        Assert.Equal("New session", model.StatusText);
    }

    [Fact]
    public async Task Connect_saves_a_new_session_first_and_calls_through()
    {
        var calls = new List<string>();
        var model = Model(calls);
        model.NewSession();
        model.Form.Name = "dev";
        model.Form.Ssh = "user@devbox";

        await model.ToggleConnectionAsync();

        Assert.True(File.Exists(Path.Combine(_directory, "dev.toml")));
        Assert.Equal(["connect dev"], calls);
    }

    [AvaloniaFact]
    public void Closing_the_window_hides_it_until_the_app_allows_the_close()
    {
        var window = new ManagerWindow(Model());
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
