using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Basin;
using Basin.Capabilities;
using Basin.UI.Avalonia;
using Basin.Diagnostics;
using Basin.Render.Skia;
using Basin.Scene;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Waylonia.Sessions;
using Waylonia.Shell;
using Waylonia.Ui;
using Xunit;

namespace Waylonia.Tests.Shell;

public sealed class NestedShellTests
{
    [AvaloniaFact]
    public void A_mapped_client_is_framed_inside_the_work_area_and_the_panels_take_their_strips()
    {
        using var log = new LogCapture();
        using var harness = new WayloniaHostHarness(nested: new WayloniaHostHarness.NestedShellOptions());
        var shell = harness.Shell!;
        var toplevel = harness.MapToplevel(width: 200, height: 150, title: "notes", appId: "org.basin.notes", serverDecorated: true);

        var window = Assert.Single(shell.Windows);
        Assert.True(window.Decorated);
        Assert.NotNull(window.Frame);
        var insets = window.Insets;
        Assert.True(insets.Top > 0, $"the frame has no title bar: {string.Join(" | ", log.Lines)}");
        Assert.True(insets.Left > 0 && insets.Right > 0 && insets.Bottom > 0, $"the frame has no borders: {insets}");
        Assert.Equal(new Box(0, 24, 800, 552), shell.WorkArea);
        Assert.True(shell.WorkArea.Contains(window.FrameBox), $"the frame {window.FrameBox} lies outside the work area");
        Assert.Equal(200, toplevel.ConfiguredWidth == 0 ? 200 : toplevel.ConfiguredWidth);
        Assert.Same(window, shell.Focused);

        var path = Path.Combine(Path.GetTempPath(), $"waylonia-shell-{Environment.ProcessId}.png");
        using var renderer = new SkiaRenderer();
        var shot = new MemoryBuffer(800, 600, DrmFormat.Xrgb8888);
        try
        {
            Assert.True(harness.Host.Scene.Render(renderer, shot, new RenderColor(0f, 0f, 0f, 1f)));
            Assert.True(shot.BeginDataAccess(BufferDataAccess.Read, out var view));
            try
            {
                var client = window.ClientBox;
                var inside = Pixel(view, client.X + 10, client.Y + 10);
                Assert.Equal(0xFF3366AAu, inside & 0xFFFFFFFF);
                var title = Pixel(view, client.X + (client.Width / 2), client.Y - (insets.Top / 2));
                Assert.NotEqual(0xFF000000u, title);
            }
            finally
            {
                shot.EndDataAccess();
            }
        }
        finally
        {
            shot.Destroy();
            File.Delete(path);
        }

        toplevel.Destroy();
        harness.PumpUntil(() => shell.Windows.Count == 0, "the managed window outlived its toplevel");
    }

    [AvaloniaFact]
    public void A_client_that_asks_for_nothing_keeps_its_own_decorations()
    {
        using var harness = new WayloniaHostHarness(nested: new WayloniaHostHarness.NestedShellOptions());
        var shell = harness.Shell!;
        var toplevel = harness.MapToplevel(title: "csd");

        var window = Assert.Single(shell.Windows);
        Assert.False(window.Decorated);
        Assert.Null(window.Frame);
        Assert.Equal(window.ClientBox, window.FrameBox);

        toplevel.Destroy();
        harness.PumpUntil(() => shell.Windows.Count == 0, "the managed window outlived its toplevel");
    }

    [AvaloniaFact]
    public void The_close_chord_reaches_the_focused_client_as_a_close()
    {
        using var harness = new WayloniaHostHarness(nested: new WayloniaHostHarness.NestedShellOptions());
        var shell = harness.Shell!;
        var toplevel = harness.MapToplevel(title: "notes");

        Key(shell, 56, true);
        Key(shell, 62, true);
        Key(shell, 62, false);
        Key(shell, 56, false);
        harness.PumpUntil(() => toplevel.CloseReceived, "Alt+F4 did not close the client");

        toplevel.Destroy();
        harness.PumpUntil(() => shell.Windows.Count == 0, "the managed window outlived its toplevel");
    }

    [AvaloniaFact]
    public void A_fractional_output_still_composes_the_frame_and_the_client()
    {
        using var harness = new WayloniaHostHarness(nested: new WayloniaHostHarness.NestedShellOptions(1334, 1000, 1.6666666666666667));
        var shell = harness.Shell!;
        var toplevel = harness.MapToplevel(width: 200, height: 150, title: "notes", serverDecorated: true);
        var window = Assert.Single(shell.Windows);
        using var renderer = new SkiaRenderer();
        var shot = new MemoryBuffer(1334, 1000, DrmFormat.Xrgb8888);
        try
        {
            Assert.True(harness.Host.Scene.Render(renderer, shot, new RenderColor(0f, 0f, 0f, 1f), shell.Scale));
            Assert.True(shot.BeginDataAccess(BufferDataAccess.Read, out var view));
            try
            {
                var client = window.ClientBox;
                var inside = Pixel(view, (int)((client.X + 10) * shell.Scale), (int)((client.Y + 10) * shell.Scale));
                Assert.Equal(0xFF3366AAu, inside);
                var title = Pixel(view, (int)((client.X + (client.Width / 2)) * shell.Scale), (int)((client.Y - (window.Insets.Top / 2)) * shell.Scale));
                Assert.NotEqual(0xFF000000u, title);
            }
            finally
            {
                shot.EndDataAccess();
            }
        }
        finally
        {
            shot.Destroy();
        }

        toplevel.Destroy();
        harness.PumpUntil(() => shell.Windows.Count == 0, "the managed window outlived its toplevel");
    }

    [AvaloniaFact]
    public void The_background_fills_the_output_in_the_configured_color_and_follows_a_resize()
    {
        using var harness = new WayloniaHostHarness(nested: new WayloniaHostHarness.NestedShellOptions(
            Settings: new ShellSettings(Background: "#023c88")));
        var shell = harness.Shell!;
        var rect = Assert.IsType<SceneRect>(Assert.Single(shell.Layers.Background.Children));
        Assert.Equal(new RenderColor(0x02 / 255f, 0x3c / 255f, 0x88 / 255f, 1f), rect.Color);
        Assert.Equal(new RenderColor(0x02 / 255f, 0x3c / 255f, 0x88 / 255f, 1f), shell.Background);
        Assert.Equal((shell.Output.Width, shell.Output.Height), (rect.Width, rect.Height));

        shell.Resize(1000, 700, 1.0);
        Assert.Equal((1000, 700), (rect.Width, rect.Height));

        shell.Apply(new ShellSettings(Background: "#ffffff"), PanelLayout.From(new PanelSettings(), BasinLogger.None), KeyTable.Build([], [], BasinLogger.None), sessionTitles: true);
        Assert.Equal(new RenderColor(1f, 1f, 1f, 1f), rect.Color);
    }

    [AvaloniaFact]
    public void An_edge_resize_stops_at_the_panels()
    {
        using var harness = new WayloniaHostHarness(nested: new WayloniaHostHarness.NestedShellOptions());
        var shell = harness.Shell!;
        var toplevel = harness.MapToplevel(width: 200, height: 150, title: "sized", serverDecorated: true);
        var window = shell.Windows[0];
        window.MoveFrameTo(window.FrameBox.X, shell.WorkArea.Y + 100);
        var before = window.FrameBox;
        var center = before.X + (before.Width / 2);

        Move(shell, center, before.Y);
        shell.BeginResize(window, global::Basin.Shell.Xdg.ResizeEdges.Top, null);
        Move(shell, center, -100);
        CommitConfigured(harness, toplevel, window);
        Button(shell, 0x110, false);
        harness.PumpInput();
        Assert.Equal(shell.WorkArea.Y, window.FrameBox.Y);
        Assert.Equal(before.Bottom, window.FrameBox.Bottom);

        var top = window.FrameBox;
        Move(shell, center, top.Bottom);
        shell.BeginResize(window, global::Basin.Shell.Xdg.ResizeEdges.Bottom, null);
        Move(shell, center, shell.Output.Bottom + 100);
        CommitConfigured(harness, toplevel, window);
        Button(shell, 0x110, false);
        harness.PumpInput();
        Assert.Equal(shell.WorkArea.Bottom, window.FrameBox.Bottom);
        Assert.Equal(top.Y, window.FrameBox.Y);

        toplevel.Destroy();
        harness.PumpUntil(() => shell.Windows.Count == 0, "the managed window outlived its toplevel");
    }

    [AvaloniaFact]
    public void A_title_drag_moves_the_window_and_a_click_on_another_focuses_it()
    {
        using var harness = new WayloniaHostHarness(nested: new WayloniaHostHarness.NestedShellOptions());
        var shell = harness.Shell!;
        var first = harness.MapToplevel(width: 200, height: 150, title: "first", serverDecorated: true);
        var second = harness.MapToplevel(width: 200, height: 150, title: "second", serverDecorated: true);
        var one = shell.Windows[0];
        var two = shell.Windows[1];
        Assert.Same(two, shell.Focused);
        Assert.True(two.FrameBox.X >= one.FrameBox.Right || two.FrameBox.Y >= one.FrameBox.Bottom, "the second window overlaps the first");

        var before = one.FrameBox;
        var titleX = before.X + (before.Width / 2);
        var titleY = before.Y + (one.Insets.Top / 2);
        Move(shell, titleX, titleY);
        Button(shell, 0x110, true);
        harness.PumpInput();
        Assert.Same(one, shell.Focused);
        Move(shell, titleX + 40, titleY + 30);
        Button(shell, 0x110, false);
        harness.PumpInput();
        Assert.Equal(before.X + 40, one.FrameBox.X);
        Assert.Equal(before.Y + 30, one.FrameBox.Y);

        var client = two.ClientBox;
        Move(shell, client.X + 5, client.Y + 5);
        Button(shell, 0x110, true);
        Button(shell, 0x110, false);
        harness.PumpInput();
        Assert.Same(two, shell.Focused);

        var top = one.FrameBox;
        Move(shell, top.X + (top.Width / 2), top.Y + (one.Insets.Top / 2));
        Button(shell, 0x110, true);
        Move(shell, top.X + (top.Width / 2), 2);
        Assert.Equal(shell.WorkArea.Y, one.FrameBox.Y);
        Move(shell, top.X + (top.Width / 2), shell.Output.Bottom + 200);
        Assert.True(one.FrameBox.Y + one.Insets.Top <= shell.WorkArea.Bottom, "the title bar left the work area at the bottom");
        Button(shell, 0x110, false);
        harness.PumpInput();

        first.Destroy();
        second.Destroy();
        harness.PumpUntil(() => shell.Windows.Count == 0, "the managed windows outlived their toplevels");
    }

    [AvaloniaFact]
    public void Alt_tab_switches_focus_and_the_workspace_chords_move_between_workspaces()
    {
        using var harness = new WayloniaHostHarness(nested: new WayloniaHostHarness.NestedShellOptions());
        var shell = harness.Shell!;
        var first = harness.MapToplevel(title: "first", serverDecorated: true);
        var second = harness.MapToplevel(title: "second", serverDecorated: true);
        var one = shell.Windows[0];
        var two = shell.Windows[1];
        Assert.Same(two, shell.Focused);

        var shown = 0;
        var hidden = 0;
        shell.SwitcherShown += (_, _) => shown++;
        shell.SwitcherHidden += () => hidden++;
        Key(shell, 56, true);
        Key(shell, 15, true);
        Key(shell, 15, false);
        Assert.True(shell.SwitcherOpen);
        Assert.Equal(1, shown);
        Key(shell, 56, false);
        harness.PumpInput();
        Assert.False(shell.SwitcherOpen);
        Assert.Equal(1, hidden);
        Assert.Same(one, shell.Focused);

        Key(shell, 29, true);
        Key(shell, 56, true);
        Key(shell, 106, true);
        Key(shell, 106, false);
        Key(shell, 56, false);
        Key(shell, 29, false);
        harness.PumpInput();
        Assert.Equal(1, shell.Workspaces.Current);
        Assert.False(one.Tree!.Enabled);
        Assert.False(two.Tree!.Enabled);
        Assert.Null(shell.Focused);

        Key(shell, 29, true);
        Key(shell, 56, true);
        Key(shell, 105, true);
        Key(shell, 105, false);
        Key(shell, 56, false);
        Key(shell, 29, false);
        harness.PumpInput();
        Assert.Equal(0, shell.Workspaces.Current);
        Assert.True(one.Tree!.Enabled);
        Assert.Same(one, shell.Focused);

        first.Destroy();
        second.Destroy();
        harness.PumpUntil(() => shell.Windows.Count == 0, "the managed windows outlived their toplevels");
    }

    [AvaloniaFact]
    public void A_client_decorated_window_that_drops_its_shadow_when_maximized_stays_pinned_to_the_work_area()
    {
        using var harness = new WayloniaHostHarness(nested: new WayloniaHostHarness.NestedShellOptions());
        var shell = harness.Shell!;
        var toplevel = harness.MapToplevel(width: 250, height: 200, title: "csd");
        var window = Assert.Single(shell.Windows);
        Assert.False(window.Decorated);

        toplevel.XdgSurface.SetWindowGeometry(25, 25, 200, 150);
        toplevel.Surface.Commit();
        harness.PumpUntil(() => window.Geometry.X == 25, "the geometry offset never reached the shell");
        var before = window.ClientBox;
        Assert.Equal(25, window.Geometry.X);

        shell.SetMaximized(window, true);
        harness.PumpUntil(() => window.Maximized && toplevel.ConfiguredWidth == shell.WorkArea.Width, "the window never maximized");
        var width = toplevel.ConfiguredWidth;
        var height = toplevel.ConfiguredHeight;
        var buffer = harness.Client.CreateBuffer(width, height, WayloniaHostHarness.Fill(width, height, 0xFF3366AA));
        toplevel.XdgSurface.SetWindowGeometry(0, 0, width, height);
        toplevel.Surface.Attach(buffer.Proxy, 0, 0);
        toplevel.Surface.Damage(0, 0, width, height);
        toplevel.Surface.Commit();
        harness.PumpUntil(() => window.Geometry.X == 0 && window.Geometry.Width == width, "the maximized buffer never arrived");

        Assert.Equal(shell.WorkArea, window.ClientBox);
        Assert.True(before.X >= 0 && before.Y >= shell.WorkArea.Y);

        toplevel.Destroy();
        harness.PumpUntil(() => shell.Windows.Count == 0, "the managed window outlived its toplevel");
    }

    [AvaloniaFact]
    public void The_panel_snapshot_follows_the_frame_once_a_maximized_or_restored_buffer_arrives()
    {
        using var harness = new WayloniaHostHarness(nested: new WayloniaHostHarness.NestedShellOptions());
        var shell = harness.Shell!;
        var toplevel = harness.MapToplevel(width: 200, height: 150, title: "notes", serverDecorated: true);
        var window = Assert.Single(shell.Windows);
        var restored = window.FrameBox;
        Box Snapshot() => Assert.Single(shell.SnapshotWindows()).Frame;
        Assert.Equal(restored, Snapshot());

        var published = 0;
        shell.Changed += () => published++;
        shell.SetMaximized(window, true);
        harness.PumpUntil(() => toplevel.ConfiguredWidth > 200, "the window never maximized");
        var publishedBeforeCommit = published;
        Commit(harness, toplevel, toplevel.ConfiguredWidth, toplevel.ConfiguredHeight);
        harness.PumpUntil(() => window.Geometry.Width == toplevel.ConfiguredWidth, "the maximized buffer never arrived");
        Assert.True(published > publishedBeforeCommit, "the committed maximized size was not published");
        Assert.Equal(shell.WorkArea, Snapshot());

        shell.SetMaximized(window, false);
        harness.PumpUntil(() => toplevel.ConfiguredWidth == 200, "the window never restored");
        publishedBeforeCommit = published;
        Commit(harness, toplevel, 200, 150);
        harness.PumpUntil(() => window.Geometry.Width == 200, "the restored buffer never arrived");
        Assert.True(published > publishedBeforeCommit, "the committed restored size was not published");
        Assert.Equal(restored, Snapshot());
        Assert.Equal(restored, window.FrameBox);

        toplevel.Destroy();
        harness.PumpUntil(() => shell.Windows.Count == 0, "the managed window outlived its toplevel");
    }

    private static void Commit(WayloniaHostHarness harness, HarnessToplevel toplevel, int width, int height)
    {
        var buffer = harness.Client.CreateBuffer(width, height, WayloniaHostHarness.Fill(width, height, 0xFF3366AA));
        toplevel.Surface.Attach(buffer.Proxy, 0, 0);
        toplevel.Surface.Damage(0, 0, width, height);
        toplevel.Surface.Commit();
    }

    [AvaloniaFact]
    public void The_host_full_screen_chord_is_the_shells_own_and_reaches_the_window()
    {
        using var harness = new WayloniaHostHarness(nested: new WayloniaHostHarness.NestedShellOptions());
        var shell = harness.Shell!;
        var toplevel = harness.MapToplevel(title: "notes", serverDecorated: true);
        var requests = 0;
        shell.HostFullScreenRequested += () => requests++;

        Key(shell, 29, true);
        Key(shell, 56, true);
        Key(shell, 28, true);
        Key(shell, 28, false);
        Key(shell, 56, false);
        Key(shell, 29, false);
        Assert.Equal(1, requests);

        toplevel.Destroy();
        harness.PumpUntil(() => shell.Windows.Count == 0, "the managed window outlived its toplevel");
    }

    [AvaloniaFact]
    public void Toggle_maximized_fills_the_work_area_and_restores()
    {
        using var harness = new WayloniaHostHarness(nested: new WayloniaHostHarness.NestedShellOptions());
        var shell = harness.Shell!;
        var toplevel = harness.MapToplevel(width: 200, height: 150, title: "notes", serverDecorated: true);
        var window = Assert.Single(shell.Windows);
        var before = window.FrameBox;

        Key(shell, 56, true);
        Key(shell, 68, true);
        Key(shell, 68, false);
        Key(shell, 56, false);
        harness.PumpUntil(() => window.Maximized && toplevel.ConfiguredWidth > 200, "the window never maximized");
        var insets = window.Insets;
        Assert.Equal(shell.WorkArea.Width - insets.Left - insets.Right, toplevel.ConfiguredWidth);
        Assert.Equal(shell.WorkArea.Height - insets.Top - insets.Bottom, toplevel.ConfiguredHeight);
        Assert.Equal(shell.WorkArea.X, window.FrameBox.X);
        Assert.Equal(shell.WorkArea.Y, window.FrameBox.Y);

        Key(shell, 56, true);
        Key(shell, 68, true);
        Key(shell, 68, false);
        Key(shell, 56, false);
        harness.PumpUntil(() => !window.Maximized, "the window never restored");
        Assert.Equal(before.X, window.FrameBox.X);
        Assert.Equal(before.Y, window.FrameBox.Y);

        toplevel.Destroy();
        harness.PumpUntil(() => shell.Windows.Count == 0, "the managed window outlived its toplevel");
    }

    [AvaloniaFact]
    public void A_chrome_window_is_framed_takes_the_keyboard_and_closes_through_its_frame()
    {
        using var harness = new WayloniaHostHarness(nested: new WayloniaHostHarness.NestedShellOptions());
        var shell = harness.Shell!;
        var client = harness.MapToplevel(title: "client", serverDecorated: true);
        var clientWindow = Assert.Single(shell.Windows);

        using var ui = BasinPlatform.Attach(new BasinPlatformOptions
        {
            Screens = new ShellScreenSource(shell.Output, shell.Scale),
            CompositorAffinity = harness.Host.Affinity,
        });
        var surface = (AvaloniaUISurface)ui.CreateSurface(new UISurfaceOptions
        {
            Target = UITargetKind.Memory,
            Width = 300,
            Height = 200,
            Scale = 1.0,
        })!;
        surface.Content = new Button { Content = "inside" };
        var configured = new List<(int Width, int Height)>();
        var moved = new List<(int X, int Y)>();
        var closed = 0;
        var content = new ChromeContent(
            surface, "Sessions", "waylonia.sessions", shell.UISurfaces, shell.Scale,
            (w, h, _) => configured.Add((w, h)),
            (x, y) => moved.Add((x, y)),
            () => closed++);

        var chrome = shell.Adopt(content);
        harness.PumpInput();
        Assert.Equal(2, shell.Windows.Count);
        Assert.True(chrome.Decorated);
        Assert.NotNull(chrome.Frame);
        Assert.True(chrome.Insets.Top > 0);
        Assert.Same(chrome, shell.Focused);
        Assert.Same(surface, shell.Router.KeyboardFocus);
        Assert.Null(harness.Host.Seat.Keyboard.Focus);
        Assert.Contains(shell.SnapshotWindows(), info => info.Title == "Sessions" && info.Focused);

        var box = clientWindow.ClientBox;
        Move(shell, box.X + 5, box.Y + 5);
        Button(shell, 0x110, true);
        Button(shell, 0x110, false);
        harness.PumpInput();
        Assert.Same(clientWindow, shell.Focused);
        Assert.Null(shell.Router.KeyboardFocus);
        Assert.Same(clientWindow.Content.Surface, harness.Host.Seat.Keyboard.Focus);

        shell.ActivateWindow(chrome);
        Assert.Same(surface, shell.Router.KeyboardFocus);
        var popup = ui.CreateSurface(new UISurfaceOptions { Target = UITargetKind.Memory, Width = 100, Height = 80, Scale = 1.0 })!;
        var dismissals = 0;
        shell.PopupDismissRequested += () => dismissals++;
        shell.PopupOpened(popup, surface);
        Move(shell, box.X + 5, box.Y + 5);
        Button(shell, 0x110, true);
        Button(shell, 0x110, false);
        harness.PumpInput();
        Assert.Equal(1, dismissals);
        Assert.Same(chrome, shell.Focused);
        shell.PopupClosed(popup);
        popup.Dispose();
        Button(shell, 0x110, true);
        Button(shell, 0x110, false);
        harness.PumpInput();
        Assert.Equal(1, dismissals);
        Assert.Same(clientWindow, shell.Focused);

        shell.ActivateWindow(chrome);
        var frame = chrome.FrameBox;
        var grabX = frame.X + (frame.Width / 2);
        var grabY = frame.Y + (chrome.Insets.Top / 2);
        Move(shell, grabX, grabY);
        Button(shell, 0x110, true);
        Move(shell, grabX + 30, grabY + 20);
        Button(shell, 0x110, false);
        Assert.Equal(frame.X + 30, chrome.FrameBox.X);
        Assert.Equal((chrome.ClientBox.X, chrome.ClientBox.Y), moved[^1]);

        var scroll = new ScrollViewer { Content = new Border { Height = 2000, Width = 100 } };
        surface.Content = scroll;
        RenderUntilHitTestable(surface, new global::Avalonia.Point(150, 100));
        Assert.True(surface.PublishDamage(), "the chrome surface rendered no frame");
        harness.PumpInput();
        var inside = chrome.ClientBox;
        Move(shell, inside.X + (inside.Width / 2), inside.Y + (inside.Height / 2));
        Assert.Same(surface, shell.Router.Hovered);
        Axis(shell, 0, -1);
        harness.PumpInput();
        Dispatcher.UIThread.RunJobs();
        Assert.True(scroll.Offset.Y > 0, $"a wheel notch down scrolled to {scroll.Offset.Y}");
        var down = scroll.Offset.Y;
        Axis(shell, 0, 1);
        harness.PumpInput();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, scroll.Offset.Y);
        Assert.True(down >= 40, $"one notch scrolled only {down}");

        var wider = chrome.ClientBox with { Width = 400 };
        chrome.ResizeClientTo(wider, global::Basin.Shell.Xdg.ResizeEdges.Right);
        Assert.Contains((400, 200), configured);

        shell.OnFrameAction(chrome, new FrameAction(FrameActionKind.Close));
        Assert.Equal(1, closed);
        shell.Release(chrome);
        Assert.Single(shell.Windows);
        Assert.Same(clientWindow, shell.Focused);
        surface.Dispose();

        client.Destroy();
        harness.PumpUntil(() => shell.Windows.Count == 0, "the managed window outlived its toplevel");
    }

    [AvaloniaFact]
    public void A_chrome_popup_closes_on_a_press_outside_it_and_a_panel_menu_takes_the_keyboard()
    {
        using var harness = new WayloniaHostHarness(nested: new WayloniaHostHarness.NestedShellOptions());
        var shell = harness.Shell!;
        var host = new Window { Width = 800, Height = 600 };
        host.Show();
        var model = new PanelModel(new FakePanelCommands());
        using var chrome = new ShellChrome(shell, host, model, action => action(), BasinLogger.None);
        var directory = Path.Combine(Path.GetTempPath(), $"waylonia-chrome-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var manager = new ManagerViewModel(
                new SessionStore(directory),
                new SessionRegistry(new StubSessionHost()),
                BasinLogger.None,
                _ => Task.FromResult<string?>(null),
                _ => Task.CompletedTask);
            chrome.OpenManager(manager);
            harness.PumpInput();
            var window = Assert.Single(shell.Windows, w => w.Content is ChromeContent);
            var surface = Assert.IsType<ChromeContent>(window.Content).Surface;
            Assert.Same(surface, shell.Router.KeyboardFocus);

            var view = Assert.IsType<ManagerView>(surface.Content);
            var menu = view.GetVisualDescendants().OfType<Menu>().First();
            var session = menu.GetVisualDescendants().OfType<MenuItem>().First();
            session.Open();
            harness.PumpInput();
            Assert.True(session.IsSubMenuOpen);
            Assert.Same(surface, shell.Router.KeyboardFocus);
            Assert.False(shell.PanelHasKeyboard);
            session.Close();
            harness.PumpInput();
            Assert.Same(surface, shell.Router.KeyboardFocus);

            session.Open();
            harness.PumpInput();
            Assert.True(session.IsSubMenuOpen);
            var desktop = window.FrameBox;
            var requested = 0;
            shell.PopupDismissRequested += () => requested++;
            Assert.True(shell.HasOpenPopups, "the popup was not registered");
            Move(shell, desktop.Right + 20, desktop.Bottom + 20);
            Button(shell, 0x110, true);
            Button(shell, 0x110, false);
            harness.PumpInput();
            Dispatcher.UIThread.RunJobs();
            harness.PumpInput();
            Assert.Equal(1, requested);
            Assert.False(session.IsSubMenuOpen);
            Assert.Same(surface, shell.Router.KeyboardFocus);

            var combo = view.GetVisualDescendants().OfType<ComboBox>().First();
            combo.IsDropDownOpen = true;
            harness.PumpInput();
            Assert.True(shell.HasOpenPopups, "the dropdown was not registered");
            Button(shell, 0x110, true);
            Button(shell, 0x110, false);
            harness.PumpInput();
            Dispatcher.UIThread.RunJobs();
            harness.PumpInput();
            Assert.Equal(2, requested);
            Assert.False(combo.IsDropDownOpen);
            Assert.False(shell.HasOpenPopups);

            var panel = shell.UISurfaces.Count > 0 ? shell.Layers.Panel.Children.OfType<global::Basin.Scene.SceneBuffer>().Select(node => shell.UISurfaces.SurfaceOf(node)).OfType<AvaloniaUISurface>().FirstOrDefault() : null;
            Assert.NotNull(panel);
            var panelMenu = ((Control)panel!.Content!).GetVisualDescendants().OfType<Menu>().First();
            var applications = panelMenu.GetVisualDescendants().OfType<MenuItem>().First();
            applications.Open();
            harness.PumpInput();
            Assert.True(shell.PanelHasKeyboard);
            Assert.Same(panel, shell.Router.KeyboardFocus);

            var places = panelMenu.GetVisualDescendants().OfType<MenuItem>().Skip(1).First();
            applications.Close();
            places.Open();
            harness.PumpInput();
            Assert.True(places.IsSubMenuOpen);
            Assert.True(shell.PanelHasKeyboard);
            Assert.Same(panel, shell.Router.KeyboardFocus);

            places.Close();
            harness.PumpInput();
            Assert.False(shell.PanelHasKeyboard);
            Assert.Same(surface, shell.Router.KeyboardFocus);
        }
        finally
        {
            host.Close();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void CommitConfigured(WayloniaHostHarness harness, HarnessToplevel toplevel, ManagedWindow window)
    {
        var height = window.Geometry.Height;
        harness.PumpUntil(() => toplevel.ConfiguredHeight != 0 && toplevel.ConfiguredHeight != height, "the resize never reached the client");
        var width = toplevel.ConfiguredWidth;
        height = toplevel.ConfiguredHeight;
        var buffer = harness.Client.CreateBuffer(width, height, WayloniaHostHarness.Fill(width, height, 0xFF3366AA));
        toplevel.Surface.Attach(buffer.Proxy, 0, 0);
        toplevel.Surface.Damage(0, 0, width, height);
        toplevel.Surface.Commit();
        harness.PumpUntil(() => window.Geometry.Height == height, "the resized buffer never arrived");
    }

    private static void Move(NestedShell shell, double x, double y) =>
        shell.HandleInput(new global::Basin.Avalonia.BasinViewInput(
            global::Basin.Avalonia.BasinViewInputKind.PointerMotion, 0, x, y, 0, false, 0, 0, 0));

    private static void RenderUntilHitTestable(AvaloniaUISurface surface, global::Avalonia.Point point)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            global::Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            if (global::Avalonia.Input.InputExtensions.InputHitTest(surface.Root!, point) is not null)
            {
                return;
            }
        }

        Assert.Fail("the chrome surface never became hit-testable");
    }

    private static void Axis(NestedShell shell, double dx, double dy) =>
        shell.HandleInput(new global::Basin.Avalonia.BasinViewInput(
            global::Basin.Avalonia.BasinViewInputKind.PointerAxis, 0, 0, 0, 0, false, dx, dy, 0));

    private static void Button(NestedShell shell, uint code, bool pressed) =>
        shell.HandleInput(new global::Basin.Avalonia.BasinViewInput(
            global::Basin.Avalonia.BasinViewInputKind.PointerButton, 0, 0, 0, code, pressed, 0, 0, 0));

    private static void Key(NestedShell shell, uint code, bool pressed) =>
        shell.HandleInput(new global::Basin.Avalonia.BasinViewInput(
            global::Basin.Avalonia.BasinViewInputKind.Key, 0, 0, 0, code, pressed, 0, 0, 0));

    private static unsafe uint Pixel(in BufferDataView view, int x, int y) =>
        *(uint*)((byte*)view.Data + (y * view.Stride) + (x * 4));
}
