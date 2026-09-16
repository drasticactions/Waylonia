using Waylonia.Shell.Applets;
using Xunit;

namespace Waylonia.Tests.Shell;

public sealed class WindowListViewModelTests
{
    private static string[] Titles(WindowListViewModel list) => list.Buttons.Select(button => button.Title).ToArray();

    [Fact]
    public void Only_windows_on_the_current_workspace_or_sticky_get_a_button_in_map_order()
    {
        var model = PanelModelFixture.Model();
        model.Windows =
        [
            PanelModelFixture.Window(1, "Editor", workspace: 0),
            PanelModelFixture.Window(2, "Mail", workspace: 1),
            PanelModelFixture.Window(3, "Player", workspace: 1, sticky: true),
            PanelModelFixture.Window(4, string.Empty, appId: "org.gnome.Terminal", workspace: 0),
        ];
        var list = new WindowListViewModel(model);

        Assert.Equal(["Editor", "Player", "org.gnome.Terminal"], Titles(list));
        Assert.Equal([1L, 3L, 4L], list.Buttons.Select(button => button.Id));
    }

    [Fact]
    public void Switching_workspace_rebuilds_the_buttons()
    {
        var model = PanelModelFixture.Model();
        model.Windows =
        [
            PanelModelFixture.Window(1, "Editor", workspace: 0),
            PanelModelFixture.Window(2, "Mail", workspace: 1),
            PanelModelFixture.Window(3, "Player", sticky: true),
        ];
        var list = new WindowListViewModel(model);

        model.CurrentWorkspace = 1;

        Assert.Equal(["Mail", "Player"], Titles(list));
    }

    [Fact]
    public void A_button_carries_the_window_state()
    {
        var model = PanelModelFixture.Model();
        model.Windows = [PanelModelFixture.Window(1, "Editor", focused: true, minimized: true, attention: true, session: "devbox")];
        var list = new WindowListViewModel(model);

        var button = Assert.Single(list.Buttons);
        Assert.True(button.IsFocused);
        Assert.True(button.IsMinimized);
        Assert.True(button.DemandsAttention);
        Assert.Equal("devbox", button.Session);
    }

    [Fact]
    public void Clicking_the_focused_button_minimizes_and_any_other_focuses()
    {
        var commands = new FakePanelCommands();
        var model = PanelModelFixture.Model(commands);
        model.Windows =
        [
            PanelModelFixture.Window(1, "Editor", focused: true),
            PanelModelFixture.Window(2, "Mail"),
            PanelModelFixture.Window(3, "Player", focused: true, minimized: true),
        ];
        var list = new WindowListViewModel(model);

        list.Buttons[0].ActivateCommand.Execute(null);
        list.Buttons[1].ActivateCommand.Execute(null);
        list.Buttons[2].ActivateCommand.Execute(null);
        list.Buttons[1].MenuCommand.Execute(null);

        Assert.Equal(["MinimizeWindow 1", "FocusWindow 2", "FocusWindow 3", "ShowWindowMenu 2"], commands.Calls);
    }

    [Fact]
    public void A_title_change_updates_the_existing_button_instead_of_replacing_it()
    {
        var model = PanelModelFixture.Model();
        model.Windows = [PanelModelFixture.Window(1, "Editor"), PanelModelFixture.Window(2, "Mail")];
        var list = new WindowListViewModel(model);
        var editor = list.Buttons[0];
        var mail = list.Buttons[1];

        model.Windows = [PanelModelFixture.Window(1, "Editor — draft", focused: true), PanelModelFixture.Window(2, "Mail")];

        Assert.Same(editor, list.Buttons[0]);
        Assert.Same(mail, list.Buttons[1]);
        Assert.Equal("Editor — draft", editor.Title);
        Assert.True(editor.IsFocused);
    }

    [Fact]
    public void Reordering_and_removal_keep_the_surviving_buttons()
    {
        var model = PanelModelFixture.Model();
        model.Windows = [PanelModelFixture.Window(1, "A"), PanelModelFixture.Window(2, "B"), PanelModelFixture.Window(3, "C")];
        var list = new WindowListViewModel(model);
        var c = list.Buttons[2];
        var a = list.Buttons[0];

        model.Windows = [PanelModelFixture.Window(3, "C"), PanelModelFixture.Window(4, "D"), PanelModelFixture.Window(1, "A")];

        Assert.Equal(["C", "D", "A"], Titles(list));
        Assert.Same(c, list.Buttons[0]);
        Assert.Same(a, list.Buttons[2]);
    }
}
