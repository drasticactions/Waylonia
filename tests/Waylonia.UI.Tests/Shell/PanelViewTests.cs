using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Waylonia.Sessions;
using Waylonia.Shell;
using Waylonia.Shell.Applets;
using Xunit;

namespace Waylonia.Tests.Shell;

public sealed class PanelViewTests
{
    private static readonly PanelApplet[] TopApplets = [new(PanelAppletKind.MenuBar)];

    private static readonly PanelApplet[] BottomApplets =
    [
        new(PanelAppletKind.WindowList),
        new(PanelAppletKind.WorkspaceSwitcher),
    ];

    private static PanelModel Model(FakePanelCommands? commands = null)
    {
        var model = PanelModelFixture.Model(commands);
        model.SettingsAvailable = true;
        model.Sessions = [PanelModelFixture.Session("devbox"), PanelModelFixture.Session("idle", SessionStatus.Disconnected)];
        model.Applications =
        [
            new ApplicationMenuItem("Sessions…", Invoke: () => { }),
            new ApplicationMenuItem(string.Empty, Separator: true),
            new ApplicationMenuItem("Utilities", Children: [new ApplicationMenuItem("Terminal", Command: "foot", Session: "devbox")]),
        ];
        model.Windows =
        [
            PanelModelFixture.Window(1, "Editor", focused: true),
            PanelModelFixture.Window(2, "Mail", workspace: 1),
            PanelModelFixture.Window(3, string.Empty, appId: "player", sticky: true, minimized: true),
        ];
        return model;
    }

    private static PanelView Panel(PanelModel model, IReadOnlyList<PanelApplet> applets, double width = 800, double height = 24) =>
        (PanelView)Layout(new PanelView(new PanelViewModel(model, applets)), width, height);

    [AvaloniaFact]
    public void The_menu_bar_shows_the_three_menus()
    {
        var panel = Panel(Model(), TopApplets);

        var menu = Find<Menu>(panel, _ => true);
        var items = menu.GetVisualDescendants().OfType<MenuItem>().ToList();

        Assert.Equal(["Applications", "Places", "System"], items.Select(item => item.Header?.ToString()));
        Assert.Equal(24, panel.Bounds.Height);
        Assert.Equal(800, panel.Bounds.Width);
    }

    [AvaloniaFact]
    public void Opening_applications_shows_its_children_with_a_separator_rendered_as_one()
    {
        var panel = Panel(Model(), TopApplets);
        var menu = Find<Menu>(panel, _ => true);
        var applications = menu.GetVisualDescendants().OfType<MenuItem>().First();

        applications.Open();
        panel.UpdateLayout();

        Assert.True(applications.IsSubMenuOpen);
        var children = Enumerable.Range(0, applications.ItemCount)
            .Select(index => Assert.IsType<MenuItem>(applications.ContainerFromIndex(index)))
            .ToList();
        Assert.Equal(["Sessions…", "-", "Utilities"], children.Select(child => child.Header?.ToString()));
        Assert.Contains(":separator", children[1].Classes);
        Assert.DoesNotContain(":separator", children[0].Classes);
        Assert.Same(panel.Model!.Applets.OfType<MenuBarViewModel>().Single().Applications.Children[0].Command, children[0].Command);
        Assert.Equal(1, children[2].ItemCount);
    }

    [AvaloniaFact]
    public void An_application_with_an_icon_shows_it_on_its_menu_item_and_a_window_on_its_button()
    {
        var icon = Path.Combine(Path.GetTempPath(), $"waylonia-icon-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(icon, Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACAQMAAABIeJ9nAAAAIGNIUk0AAHomAACAhAAA+gAAAIDoAAB1MAAA6mAAADqYAAAXcJy6UTwAAAADUExURf8AABniCTcAAAAHdElNRQfqCRAGMDI1HrxrAAAAJXRFWHRkYXRlOmNyZWF0ZQAyMDI2LTA5LTE2VDA2OjQ4OjUwKzAwOjAwPxLhxwAAACV0RVh0ZGF0ZTptb2RpZnkAMjAyNi0wOS0xNlQwNjo0ODo1MCswMDowME5PWXsAAAAodEVYdGRhdGU6dGltZXN0YW1wADIwMjYtMDktMTZUMDY6NDg6NTArMDA6MDAZWnikAAAADElEQVQI12NgYGAAAAAEAAEnNCcKAAAAAElFTkSuQmCC"));
        try
        {
            var model = Model();
            model.Applications = [new ApplicationMenuItem("Terminal", Command: "foot", Session: "devbox", Icon: icon)];
            model.Windows = [PanelModelFixture.Window(1, "Editor", focused: true) with { Icon = icon }];
            var panel = Panel(model, [new PanelApplet(PanelAppletKind.MenuBar), new PanelApplet(PanelAppletKind.WindowList)]);
            var menu = Find<Menu>(panel, _ => true);
            var applications = menu.GetVisualDescendants().OfType<MenuItem>().First();
            applications.Open();
            panel.UpdateLayout();

            var terminal = Assert.IsType<MenuItem>(applications.ContainerFromIndex(0));
            var image = Assert.IsType<Image>(terminal.Icon);
            Assert.NotNull(image.Source);

            var button = Find<ToggleButton>(panel, _ => true);
            var buttonImage = Find<Image>(button, _ => true);
            Assert.NotNull(buttonImage.Source);
            Assert.True(buttonImage.IsVisible);
        }
        finally
        {
            File.Delete(icon);
        }
    }

    [AvaloniaFact]
    public void System_and_places_bind_their_entries_and_tooltips()
    {
        var commands = new FakePanelCommands();
        var panel = Panel(Model(commands), TopApplets);
        var menu = Find<Menu>(panel, _ => true);
        var roots = menu.GetVisualDescendants().OfType<MenuItem>().ToList();

        roots[2].Open();
        panel.UpdateLayout();
        var system = Enumerable.Range(0, roots[2].ItemCount).Select(index => (MenuItem)roots[2].ContainerFromIndex(index)!).ToList();
        Assert.Equal(["Sessions…", "Settings…", "-", "Disconnect devbox", "-", "Quit"], system.Select(item => item.Header?.ToString()));
        system[^1].Command!.Execute(null);
        Assert.Equal(["Quit"], commands.Calls);
        roots[2].Close();

        roots[1].Open();
        panel.UpdateLayout();
        var devbox = (MenuItem)roots[1].ContainerFromIndex(0)!;
        Assert.Equal("devbox", devbox.Header);
        devbox.Open();
        panel.UpdateLayout();
        var home = (MenuItem)devbox.ContainerFromIndex(0)!;
        Assert.Equal("Home", home.Header);
        Assert.Equal("Opens ~ with xdg-open on devbox", ToolTip.GetTip(home));
    }

    [AvaloniaFact]
    public void The_window_list_has_a_button_per_visible_window()
    {
        var panel = Panel(Model(), BottomApplets);

        var buttons = panel.GetVisualDescendants().OfType<ToggleButton>().ToList();

        Assert.Equal(2, buttons.Count);
        Assert.Equal(["Editor", "player"], buttons.Select(button => Find<TextBlock>(button, _ => true).Text));
        Assert.True(buttons[0].IsChecked);
        Assert.False(buttons[1].IsChecked);
        Assert.Contains("minimized", buttons[1].Classes);
        Assert.DoesNotContain("minimized", buttons[0].Classes);
        Assert.True(buttons[0].Bounds.Width <= 160);
        Assert.True(buttons[0].Bounds.Width >= 40);
        var first = buttons[0].TranslatePoint(default, panel)!.Value;
        var second = buttons[1].TranslatePoint(default, panel)!.Value;
        Assert.True(first.X < second.X, "buttons lay out left to right");
    }

    [AvaloniaFact]
    public void A_window_button_click_and_a_right_click_reach_the_commands()
    {
        var commands = new FakePanelCommands();
        var panel = Panel(Model(commands), BottomApplets);
        var buttons = panel.GetVisualDescendants().OfType<ToggleButton>().ToList();

        buttons[0].Command!.Execute(null);
        buttons[1].RaiseEvent(new Avalonia.Input.ContextRequestedEventArgs { Source = buttons[1] });

        Assert.Equal(["MinimizeWindow 1", "ShowWindowMenu 3"], commands.Calls);
    }

    [AvaloniaFact]
    public void The_workspace_switcher_has_a_cell_per_workspace_with_miniatures()
    {
        var model = Model();
        model.WorkspaceRows = 2;
        var panel = Panel(model, BottomApplets);

        var cells = panel.GetVisualDescendants().OfType<Border>().Where(border => border.Classes.Contains("cell")).ToList();

        Assert.Equal(4, cells.Count);
        Assert.Contains("current", cells[0].Classes);
        Assert.DoesNotContain("current", cells[1].Classes);
        Assert.Equal(2, Find<UniformGrid>(panel, grid => grid.Rows == 2).Rows);
        var miniatures = cells[0].GetVisualDescendants().OfType<Border>().Where(border => border.Classes.Contains("miniature")).ToList();
        Assert.Single(miniatures);
        Assert.Contains("focused", miniatures[0].Classes);
        var switcher = panel.Model!.Applets.OfType<WorkspaceSwitcherViewModel>().Single();
        Assert.Equal(switcher.CellWidth, cells[0].Bounds.Width);
        var cellOrigin = cells[0].TranslatePoint(default, panel)!.Value;
        var listOrigin = panel.GetVisualDescendants().OfType<ToggleButton>().First().TranslatePoint(default, panel)!.Value;
        Assert.True(cellOrigin.X > listOrigin.X, "the switcher sits after the window list");
    }

    [AvaloniaFact]
    public void The_window_list_takes_the_room_the_other_applets_leave()
    {
        var panel = Panel(Model(), [new(PanelAppletKind.WindowList), new(PanelAppletKind.Clock), new(PanelAppletKind.WorkspaceSwitcher)]);

        var strip = Find<AppletStrip>(panel, _ => true);
        var list = strip.Children[0];
        var clock = strip.Children[1];
        var switcher = strip.Children[2];

        Assert.Equal(800 - 4 - clock.Bounds.Width - switcher.Bounds.Width, list.Bounds.Width, 0.5);
        Assert.Equal(list.Bounds.Right, clock.Bounds.X, 0.5);
        Assert.Equal(clock.Bounds.Right, switcher.Bounds.X, 0.5);
        Assert.Equal(800 - 4, switcher.Bounds.Right, 0.5);
    }

    [AvaloniaFact]
    public void A_spacer_pushes_what_follows_to_the_far_end_and_the_clock_ticks_while_attached()
    {
        var model = Model();
        var panel = Panel(model, [new(PanelAppletKind.MenuBar), new(PanelAppletKind.Spacer), new(PanelAppletKind.Clock)]);

        var clock = Find<TextBlock>(panel, text => text.Classes.Contains("clock"));
        var position = clock.TranslatePoint(default, panel)!.Value;
        Assert.True(position.X + clock.Bounds.Width >= 780, "the clock sits at the right edge");
        Assert.Matches("^[0-9]{2}:[0-9]{2}$", clock.Text);
        Assert.True(panel.Model!.Applets.OfType<ClockViewModel>().Single().IsRunning);
    }

    [AvaloniaFact]
    public void The_panel_takes_its_colors_from_the_theme()
    {
        var panel = Panel(Model(), TopApplets);

        var strip = panel.FindControl<Border>("Strip")!;
        var expected = panel.FindResource("BcBgBrush");

        Assert.Same(expected, strip.Background);
    }

    private static T Find<T>(Control root, Func<T, bool> predicate)
        where T : Control =>
        root.GetVisualDescendants().OfType<T>().First(predicate);

    private static Control Layout(Control content, double width, double height)
    {
        var root = new Window
        {
            Width = width,
            Height = height,
            Content = content,
        };

        root.Show();
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();
        return content;
    }
}
