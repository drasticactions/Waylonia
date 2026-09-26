using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Basin.Hosted;
using BluerCurve.Chrome;
using Waylonia.Shell;
using Xunit;

namespace Waylonia.Tests.Shell;

public sealed class ShellWindowTests
{
    private static Point TitleBarPoint(Window window)
    {
        window.UpdateLayout();
        var titleBar = window.GetVisualDescendants().OfType<Control>().First(control => control.Name == "PART_TitleBar");
        return titleBar.TranslatePoint(new Point(titleBar.Bounds.Width / 2, titleBar.Bounds.Height / 2), window)!.Value;
    }

    [AvaloniaFact]
    public void A_plain_bluercurve_window_maximizes_on_a_title_bar_double_click()
    {
        var window = new BluerCurveWindow { Width = 400, Height = 300 };
        window.Show();
        var point = TitleBarPoint(window);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Assert.Equal(WindowState.Maximized, window.WindowState);
        window.Close();
    }

    [AvaloniaFact]
    public void The_shell_window_maximizes_on_a_title_bar_double_click()
    {
        WayloniaHostHarness.SkipWithoutWaylandClient();
        using var host = new BasinCompositorHost(new BasinCompositorOptions { AppName = "waylonia-tests" });
        var window = new ShellWindow(host, ShellWindowState.Default, h => h.CreateViewOutput(400, 300, 1.0, "shell"));
        window.Show();
        var point = TitleBarPoint(window);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Assert.Equal(WindowState.Maximized, window.WindowState);
        window.AllowClose();
        window.Close();
    }

    [AvaloniaFact]
    public void An_agent_shell_window_keeps_its_output_size_and_shows_resume_only_while_paused()
    {
        WayloniaHostHarness.SkipWithoutWaylandClient();
        using var host = new BasinCompositorHost(new BasinCompositorOptions { AppName = "waylonia-tests" });
        var window = new ShellWindow(host, new ShellWindowState(640, 400, null, null, false), h => h.CreateViewOutput(640, 400, 1.0, "shell"));
        var resumed = 0;
        window.FixForAgent(640, 400, () => resumed++);
        window.Show();
        for (var i = 0; i < 4 && window.View.Bounds.Size != new Size(640, 400); i++)
        {
            window.UpdateLayout();
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }

        Assert.False(window.CanFullScreen);
        Assert.False(window.CanResize);
        window.ToggleFullScreen();
        Assert.NotEqual(WindowState.FullScreen, window.WindowState);
        Assert.Equal(new Size(640, 400), window.View.Bounds.Size);

        var resume = window.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Resume agent"));
        Assert.False(resume.IsVisible);
        window.SetAgentStatus("Agent: paused", paused: true);
        Assert.True(resume.IsVisible);
        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Agent: paused");
        resume.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(1, resumed);
        window.SetAgentStatus("Agent: driving", paused: false);
        Assert.False(resume.IsVisible);
        window.AllowClose();
        window.Close();
    }
}
