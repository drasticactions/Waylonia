using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Basin.Diagnostics;
using Waylonia.Sessions;
using Waylonia.Tests.Ssh;
using Waylonia.UI;
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
        Assert.Equal("dev", window.View.FindControl<TextBox>("NameBox")!.Text);
    }

    [Fact]
    public void Each_problem_is_reported_in_plain_words_under_its_own_field()
    {
        var model = Model();
        model.NewSession();
        Assert.False(model.Save());
        Assert.NotNull(model.Form.NameProblem);
        Assert.Contains("user@host", model.Form.SshProblem, StringComparison.Ordinal);

        var form = model.Form;
        form.Name = "user@host";
        form.Ssh = "user@host";
        form.Video = "mpeg";
        form.Lang = "C.UTF-8; rm -rf /";
        form.DesktopIndex = SessionFormViewModel.DesktopChoices.ToList().IndexOf("custom");
        form.DesktopSize = "wide";
        form.Hotkeys = "hyper+t = foot";
        Assert.False(model.Save());

        Assert.Contains("'.', '_' and '-'", form.NameProblem, StringComparison.Ordinal);
        Assert.Null(form.SshProblem);
        Assert.Contains("h264", form.VideoProblem, StringComparison.Ordinal);
        Assert.Contains("en_US.UTF-8", form.LangProblem, StringComparison.Ordinal);
        Assert.NotNull(form.CommandProblem);
        Assert.Contains("WIDTHxHEIGHT", form.DesktopSizeProblem, StringComparison.Ordinal);
        Assert.Contains("'hyper+t'", form.HotkeysProblem, StringComparison.Ordinal);
        Assert.Equal(6, form.Problems.Count);
        Assert.Null(model.Problem);

        form.Name = "dev";
        form.Video = "h264,hw";
        form.Lang = "en_US.UTF-8";
        form.Command = "my-session";
        form.DesktopSize = "1920x1080";
        form.Hotkeys = "ctrl+alt+t";
        Assert.False(model.Save());
        Assert.Contains("'ctrl+alt+t'", form.HotkeysProblem, StringComparison.Ordinal);

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
        Assert.Contains("dev", model.Form.NameProblem, StringComparison.Ordinal);
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
    public void The_toolbar_carries_every_command_and_the_row_menu_the_ones_for_a_row()
    {
        var window = new ManagerWindow(Model());
        window.Show();

        var toolbar = window.View.FindControl<CommandBar>("Toolbar")!;
        Assert.Equal(
            ["New", "Save", "Delete", "Connect", "Import key…", "Settings"],
            toolbar.PrimaryCommands.OfType<CommandBarButton>().Select(button => button.Label));
        Assert.Same(window.Model.NewSessionCommand, window.View.FindControl<CommandBarButton>("NewButton")!.Command);
        Assert.Same(window.Model.SaveSessionCommand, window.View.FindControl<CommandBarButton>("SaveButton")!.Command);
        Assert.Same(window.Model.DeleteSessionCommand, window.View.FindControl<CommandBarButton>("DeleteButton")!.Command);
        var connect = window.View.FindControl<CommandBarButton>("ConnectButton")!;
        Assert.Same(window.Model.ToggleConnectionCommand, connect.Command);
        Assert.Equal("Connect", connect.Label);
        var settings = window.View.FindControl<CommandBarButton>("SettingsButton")!;
        Assert.Same(window.Model.OpenSettingsCommand, settings.Command);
        Assert.False(window.Model.OpenSettingsCommand.CanExecute(null));

        var rowItems = window.View.FindControl<ContextMenu>("RowMenu")!.Items.OfType<MenuItem>().ToList();
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
        var list = window.View.FindControl<ListBox>("SessionList")!;
        var rowMenu = window.View.FindControl<ContextMenu>("RowMenu")!;
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
    public async Task A_failed_connect_shows_the_link_sentence_under_the_status()
    {
        var host = new StubSessionHost { Settings = new HostSettings(TrayApps: false) };
        host.Links.ConnectOutcomes.Enqueue(new SshLinkException(
            SshLinkReason.HostKeyChanged, SshLinkException.Sentence(SshLinkReason.HostKeyChanged, "user@devbox")));
        var registry = new SessionRegistry(host);
        new SessionStore(_directory).Save(new SessionProfile("dev", "user@devbox"));
        var model = new ManagerViewModel(
            new SessionStore(_directory),
            registry,
            BasinLogger.None,
            _ => Task.FromResult<string?>(null),
            _ => Task.CompletedTask);

        Assert.False(await registry.ConnectAsync(StubSessionHost.For("dev") with { Ssh = "user@devbox" }));
        model.Reload();
        model.Select("dev");

        Assert.Contains("devbox", model.StatusText, StringComparison.Ordinal);
        Assert.Contains("~/.ssh/known_hosts", model.StatusText, StringComparison.Ordinal);
        Assert.Contains("~/.ssh/known_hosts", model.Rows.Single(row => row.Name == "dev").Detail, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void Import_key_is_hidden_on_a_desktop_and_shown_in_the_toolbar_where_keys_are_imported()
    {
        var plain = new ManagerWindow(Model());
        plain.Show();
        Assert.False(plain.Model.CanImportKey);
        Assert.False(plain.View.FindControl<CommandBarButton>("ImportKeyButton")!.IsVisible);
        plain.Close();

        var picked = new List<PickedKey>();
        var model = new ManagerViewModel(
            new SessionStore(_directory),
            new SessionRegistry(new StubSessionHost()),
            BasinLogger.None,
            _ => Task.FromResult<string?>(null),
            _ => Task.CompletedTask,
            null,
            () => Task.FromResult<IReadOnlyList<PickedKey>>(picked),
            Path.Combine(_directory, "ssh"));
        var importing = new ManagerWindow(model);
        importing.Show();
        Assert.True(model.CanImportKey);
        var button = importing.View.FindControl<CommandBarButton>("ImportKeyButton")!;
        Assert.True(button.IsVisible);
        Assert.Same(model.ImportKeyCommand, button.Command);
        importing.Close();
    }

    [Fact]
    public async Task Importing_keys_copies_each_picked_file_and_reports_every_result()
    {
        var ssh = Path.Combine(_directory, "ssh");
        var picked = new List<PickedKey>
        {
            new("id_ed25519", new MemoryStream("key one"u8.ToArray())),
            new("id_ed25519.pub", new MemoryStream("key one pub"u8.ToArray())),
        };
        var model = new ManagerViewModel(
            new SessionStore(_directory),
            new SessionRegistry(new StubSessionHost()),
            BasinLogger.None,
            _ => Task.FromResult<string?>(null),
            _ => Task.CompletedTask,
            null,
            () => Task.FromResult<IReadOnlyList<PickedKey>>(picked),
            ssh);

        await model.ImportKeyAsync();

        Assert.Equal("key one", File.ReadAllText(Path.Combine(ssh, "id_ed25519")));
        Assert.Equal("key one pub", File.ReadAllText(Path.Combine(ssh, "id_ed25519.pub")));
        var lines = model.Problem!.Split('\n');
        Assert.Equal(2, lines.Length);
        Assert.Contains("id_ed25519", lines[0], StringComparison.Ordinal);
        Assert.Contains("id_ed25519.pub", lines[1], StringComparison.Ordinal);

        picked.Clear();
        picked.Add(new PickedKey("id_ed25519", new MemoryStream("again"u8.ToArray())));
        await model.ImportKeyAsync();
        Assert.Contains("id_ed25519", model.Problem, StringComparison.Ordinal);
        Assert.Equal("key one", File.ReadAllText(Path.Combine(ssh, "id_ed25519")));
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

        Assert.Contains(model.Rows, row => row.Name == "bad" && row.Broken is not null && row.Detail.Length > 0);
        model.Select("bad");
        Assert.NotNull(model.Problem);
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

    [AvaloniaFact]
    public void Where_wsl_is_reachable_the_form_offers_the_kind_and_lists_the_distributions()
    {
        var model = new ManagerViewModel(
            new SessionStore(_directory),
            new SessionRegistry(new StubSessionHost()),
            BasinLogger.None,
            _ => Task.FromResult<string?>(null),
            _ => Task.CompletedTask,
            targets: new Dictionary<DestinationKind, ITargetLinkFactory>
            {
                [DestinationKinds.Wsl] = new FakeSshLinkFactory { Listed = ["Ubuntu-26.04", "Debian"] },
                [DestinationKinds.Incus] = new FakeSshLinkFactory { IsAvailable = false },
            });
        var window = new ManagerWindow(model);
        window.Show();
        model.NewSession();
        var kind = window.View.FindControl<ComboBox>("KindBox")!;
        var distro = window.View.FindControl<ComboBox>("TargetBox")!;
        Assert.True(Shown(kind));
        Assert.False(Shown(distro));

        Dispatcher.UIThread.RunJobs();
        Assert.Equal([DestinationKinds.Ssh, DestinationKinds.Wsl], model.Form.Kinds);
        kind.SelectedIndex = 1;
        model.Form.Name = "ubuntu";
        model.Form.Target = "Ubuntu-26.04";
        Dispatcher.UIThread.RunJobs();

        Assert.True(Shown(distro));
        Assert.Equal(["Ubuntu-26.04", "Debian"], model.Form.Targets);
        Assert.Equal("wsl:Ubuntu-26.04", model.Form.Validate()!.Ssh);
        window.Close();
    }

    private static bool Shown(Control control) =>
        control.GetSelfAndLogicalAncestors().OfType<Control>().All(ancestor => ancestor.IsVisible);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
