using Waylonia.Sessions;
using Waylonia.Shell;
using Waylonia.Shell.Applets;
using Xunit;

namespace Waylonia.Tests.Shell;

public sealed class MenuBarViewModelTests
{
    private static string[] Headers(MenuEntryViewModel root) => root.Children.Select(child => child.Header).ToArray();

    [Fact]
    public void The_bar_has_applications_places_and_system_in_that_order()
    {
        var bar = new MenuBarViewModel(PanelModelFixture.Model());

        Assert.Equal(["Applications", "Places", "System"], bar.Roots.Select(root => root.Header));
        Assert.Same(bar.Applications, bar.Roots[0]);
        Assert.Same(bar.Places, bar.Roots[1]);
        Assert.Same(bar.System, bar.Roots[2]);
    }

    [Fact]
    public void Applications_converts_submenus_invokes_launches_and_separators()
    {
        var commands = new FakePanelCommands();
        var model = PanelModelFixture.Model(commands);
        var invoked = 0;
        model.Applications =
        [
            new ApplicationMenuItem("Sessions…", Invoke: () => invoked++),
            new ApplicationMenuItem(string.Empty, Separator: true),
            new ApplicationMenuItem("Utilities", Children:
            [
                new ApplicationMenuItem("Terminal", Command: "foot", Session: "devbox"),
                new ApplicationMenuItem("Nothing"),
            ]),
        ];
        var bar = new MenuBarViewModel(model);

        var entries = bar.Applications.Children;
        Assert.Equal(3, entries.Count);
        Assert.Equal("Sessions…", entries[0].Header);
        Assert.Empty(entries[0].Children);
        entries[0].Command!.Execute(null);
        Assert.Equal(1, invoked);

        Assert.True(entries[1].IsSeparator);
        Assert.Equal(MenuEntryViewModel.SeparatorHeader, entries[1].Header);
        Assert.Null(entries[1].Command);

        var utilities = entries[2];
        Assert.Equal("Utilities", utilities.Header);
        Assert.Null(utilities.Command);
        Assert.Equal(["Terminal", "Nothing"], Headers(utilities));
        utilities.Children[0].Command!.Execute(null);
        Assert.Equal(["Launch devbox Terminal foot"], commands.Calls);
        Assert.False(utilities.Children[1].IsEnabled);
        Assert.Null(utilities.Children[1].Command);
    }

    [Fact]
    public void An_empty_application_list_shows_one_disabled_entry()
    {
        var bar = new MenuBarViewModel(PanelModelFixture.Model());

        var entry = Assert.Single(bar.Applications.Children);
        Assert.Equal("No applications", entry.Header);
        Assert.False(entry.IsEnabled);
    }

    [Fact]
    public void Applications_follow_the_model()
    {
        var model = PanelModelFixture.Model();
        var bar = new MenuBarViewModel(model);

        model.Applications = [new ApplicationMenuItem("Editor", Command: "gedit")];

        Assert.Equal(["Editor"], Headers(bar.Applications));
    }

    [Fact]
    public void Places_lists_every_connected_session_with_the_places_and_a_tooltip()
    {
        var commands = new FakePanelCommands();
        var model = PanelModelFixture.Model(commands);
        model.Sessions =
        [
            PanelModelFixture.Session("devbox"),
            PanelModelFixture.Session("idle", SessionStatus.Disconnected),
            PanelModelFixture.Session("lab"),
        ];
        var bar = new MenuBarViewModel(model);

        Assert.Equal(["devbox", "lab"], Headers(bar.Places));
        var devbox = bar.Places.Children[0];
        Assert.Equal(Places.Entries.Select(place => place.Label), Headers(devbox));
        var trash = devbox.Children[^1];
        Assert.Equal("Opens trash:/// with xdg-open on devbox", trash.ToolTip);
        trash.Command!.Execute(null);
        devbox.Children[0].Command!.Execute(null);
        Assert.Equal(["OpenPlace devbox trash:///", "OpenPlace devbox ~"], commands.Calls);
    }

    [Fact]
    public void Places_without_a_connected_session_is_one_disabled_entry()
    {
        var model = PanelModelFixture.Model();
        model.Sessions = [PanelModelFixture.Session("idle", SessionStatus.Disconnected)];
        var bar = new MenuBarViewModel(model);

        var entry = Assert.Single(bar.Places.Children);
        Assert.Equal("No session connected", entry.Header);
        Assert.False(entry.IsEnabled);

        model.Sessions = [PanelModelFixture.Session("idle")];
        Assert.Equal(["idle"], Headers(bar.Places));
    }

    [Fact]
    public void System_has_settings_only_when_available_a_disconnect_per_live_session_and_quit_last()
    {
        var commands = new FakePanelCommands();
        var model = PanelModelFixture.Model(commands);
        model.Sessions =
        [
            PanelModelFixture.Session("devbox"),
            PanelModelFixture.Session("idle", SessionStatus.Disconnected),
            PanelModelFixture.Session("starting", SessionStatus.Connecting),
        ];
        var bar = new MenuBarViewModel(model);

        Assert.Equal(["Sessions…", "-", "Disconnect devbox", "Disconnect starting", "-", "Quit"], Headers(bar.System));
        Assert.True(bar.System.Children[1].IsSeparator);

        model.SettingsAvailable = true;
        Assert.Equal(["Sessions…", "Settings…", "-", "Disconnect devbox", "Disconnect starting", "-", "Quit"], Headers(bar.System));

        model.Sessions = [];
        Assert.Equal(["Sessions…", "Settings…", "-", "Quit"], Headers(bar.System));
        Assert.Equal("Quit", bar.System.Children[^1].Header);
    }

    [Fact]
    public void System_entries_reach_the_commands()
    {
        var commands = new FakePanelCommands();
        var model = PanelModelFixture.Model(commands);
        model.SettingsAvailable = true;
        model.Sessions = [PanelModelFixture.Session("devbox")];
        var bar = new MenuBarViewModel(model);

        foreach (var entry in bar.System.Children.Where(entry => !entry.IsSeparator))
        {
            entry.Command!.Execute(null);
        }

        Assert.Equal(["OpenManager", "OpenSettings", "Disconnect devbox", "Quit"], commands.Calls);
    }

    [Fact]
    public void The_system_and_places_entries_carry_the_panel_icons()
    {
        var model = PanelModelFixture.Model();
        model.SettingsAvailable = true;
        model.Sessions = [PanelModelFixture.Session("devbox")];
        model.Icons = new PanelIcons("/i/sessions.svg", "/i/settings.svg", "/i/disconnect.svg", "/i/quit.svg", "/i/computer.svg", path => $"/i/{path.Length}.svg");
        var menu = new MenuBarViewModel(model);

        Assert.Equal("/i/sessions.svg", menu.System.Children[0].IconPath);
        Assert.Equal("/i/settings.svg", menu.System.Children[1].IconPath);
        Assert.Equal("/i/quit.svg", menu.System.Children[^1].IconPath);
        Assert.Contains(menu.System.Children, entry => entry.IconPath == "/i/disconnect.svg");
        var session = Assert.Single(menu.Places.Children);
        Assert.Equal("/i/computer.svg", session.IconPath);
        Assert.Equal("/i/1.svg", session.Children[0].IconPath);
    }
}
