using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Basin;
using Basin.Capabilities;
using Basin.UI.Avalonia;
using Basin.Diagnostics;
using Basin.Render.Skia;
using Basin.Scene;
using Basin.Shell.Nested;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Waylonia.Sessions;
using Waylonia.Shell;
using Waylonia.UI;
using Xunit;

namespace Waylonia.Tests.Shell;

public sealed class NestedShellChromeTests
{
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
        var hostWindow = new Window { Width = 800, Height = 600 };
        var host = new TestShellHost(hostWindow);
        hostWindow.Show();
        var model = new PanelModel(new FakePanelCommands());
        using var chrome = new ShellChrome(shell, host, model, PanelArrangement.From(new PanelSettings(), BasinLogger.None), action => action(), BasinLogger.None);
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
            hostWindow.Close();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void Move(NestedShell shell, double x, double y) =>
        shell.HandleInput(new global::Basin.Hosted.BasinViewInput(
            global::Basin.Hosted.BasinViewInputKind.PointerMotion, 0, x, y, 0, false, 0, 0, 0));

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
        shell.HandleInput(new global::Basin.Hosted.BasinViewInput(
            global::Basin.Hosted.BasinViewInputKind.PointerAxis, 0, 0, 0, 0, false, dx, dy, 0));

    private static void Button(NestedShell shell, uint code, bool pressed) =>
        shell.HandleInput(new global::Basin.Hosted.BasinViewInput(
            global::Basin.Hosted.BasinViewInputKind.PointerButton, 0, 0, 0, code, pressed, 0, 0, 0));
}
