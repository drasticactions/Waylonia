using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Basin.Hosted;
using Waylonia.Shell;
using Xunit;

namespace Waylonia.Tests.Shell;

public sealed class ShellWindowTests
{
    private static BasinCompositorHost NestedHost() => new(new BasinCompositorOptions
    {
        AppName = "waylonia-tests",
        ConfigureServices = static (_, services) => ShellGlobals.WithoutLayerShell(ShellGlobals.WithoutSessionLock(services)),
    });

    [AvaloniaFact]
    public void The_shell_window_leaves_its_frame_to_the_host()
    {
        WayloniaHostHarness.SkipWithoutWaylandClient();
        using var host = NestedHost();
        var window = new ShellWindow(host, ShellWindowState.Default, h => h.CreateViewOutput(400, 300, 1.0, "shell"));
        window.Show();
        Assert.Equal(WindowDecorations.Full, window.WindowDecorations);
        Assert.False(window.ExtendClientAreaToDecorationsHint);
        Assert.Same(window.View, window.Content);
        window.AllowClose();
        window.Close();
    }

    [AvaloniaFact]
    public void An_agent_shell_window_keeps_its_output_size_and_shows_resume_only_while_paused()
    {
        WayloniaHostHarness.SkipWithoutWaylandClient();
        using var host = NestedHost();
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
