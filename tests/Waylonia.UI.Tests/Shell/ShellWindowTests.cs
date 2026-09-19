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
}
