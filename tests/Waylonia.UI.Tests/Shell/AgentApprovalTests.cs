using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Basin.Diagnostics;
using Waylonia.Agent;
using Waylonia.Shell;
using Xunit;

namespace Waylonia.Tests.Shell;

public sealed class AgentApprovalTests
{
    [AvaloniaTheory]
    [InlineData("YesButton", "AllowOnce")]
    [InlineData("YesRunButton", "AllowRun")]
    [InlineData("NoButton", "Deny")]
    public void Each_button_answers_once(string button, string choice)
    {
        var expected = Enum.Parse<AgentApprovalChoice>(choice);
        var start = new DateTime(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);
        var now = start;
        var prompt = AgentApprovalText.For("windows/close", """{"id":7}""", TimeSpan.FromMinutes(2), window: "da@mini-lab:~  (foot)");
        var view = new ApprovalView(prompt, () => now);
        var answers = new List<AgentApprovalChoice>();
        view.Answered += answers.Add;
        var control = view.FindControl<Button>(button)!;
        control.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        control.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal([expected], answers);
        Assert.Equal("Close window", view.FindControl<TextBlock>("HeadingText")!.Text);
        Assert.Equal("da@mini-lab:~  (foot)", view.FindControl<SelectableTextBlock>("SubjectText")!.Text);
        Assert.Equal("Yes, and don't ask again for closing windows this run", view.FindControl<Button>("YesRunButton")!.Content);
        Assert.Equal("windows/close\n{\"id\":7}", view.FindControl<SelectableTextBlock>("DetailsText")!.Text);
        Assert.False(view.FindControl<Expander>("DetailsExpander")!.IsExpanded);
        Assert.Equal("No answer in 2:00 means No.", view.FindControl<TextBlock>("CountdownText")!.Text);
        now = start.AddSeconds(18.5);
        view.Tick();
        Assert.Equal("No answer in 1:42 means No.", view.FindControl<TextBlock>("CountdownText")!.Text);
    }

    [AvaloniaFact]
    public void The_approval_dialog_is_hidden_from_capture_and_from_the_window_list_and_answers_through_its_buttons()
    {
        using var harness = new WayloniaHostHarness(nested: new WayloniaHostHarness.NestedShellOptions(
            Settings: AgentShell.Settings(new AgentProfile("test", "/p"), new Basin.Shell.Nested.ShellSettings())));
        var shell = harness.Shell!;
        var client = harness.MapToplevel(title: "client");
        var hostWindow = new Window { Width = 800, Height = 600 };
        hostWindow.Show();
        var model = new PanelModel(new FakePanelCommands());
        using var chrome = new ShellChrome(shell, new TestShellHost(hostWindow), model, AgentShell.Arrangement, action => action(), BasinLogger.None);
        try
        {
            var answers = new List<AgentApprovalChoice>();
            chrome.Approve(AgentApprovalText.For("windows/close", """{"id":1}""", TimeSpan.FromMinutes(2)), answers.Add);
            harness.PumpInput();
            var dialog = Assert.Single(shell.Windows, window => window.Content is ChromeContent);
            Assert.False(dialog.Maximized);
            Assert.True(dialog.Content.Centered);
            Assert.True(Basin.Scene.Scene.IsCaptureExcluded(dialog.Tree!));
            if (shell.Toplevels is { } model2)
            {
                var ids = new Basin.Capabilities.ToplevelInfo[8];
                var count = model2.Enumerate(ids);
                Assert.DoesNotContain(ids.Take(count), info => info.Title == "Agent approval");
            }

            var frame = dialog.FrameBox;
            Assert.True(harness.Host.Scene.IsCaptureExcludedAt(frame.X + (frame.Width / 2), frame.Y + (dialog.Insets.Top / 2)));
            var client2 = shell.Windows.Single(window => window.Content is not ChromeContent).ClientBox;
            Assert.False(harness.Host.Scene.IsCaptureExcludedAt(client2.X + 2, client2.Bottom - 2));

            var view = Assert.IsType<ApprovalView>(((ChromeContent)dialog.Content).Surface.Content);
            view.FindControl<Button>("YesButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            harness.PumpInput();
            Assert.Equal([AgentApprovalChoice.AllowOnce], answers);
            Assert.DoesNotContain(shell.Windows, window => window.Content is ChromeContent);

            var closeFromElsewhere = chrome.Approve(AgentApprovalText.For("process/kill", """{"launch_id":1}""", TimeSpan.FromMinutes(2)), answers.Add);
            harness.PumpInput();
            Assert.Contains(shell.Windows, window => window.Content is ChromeContent);
            closeFromElsewhere();
            harness.PumpInput();
            Assert.DoesNotContain(shell.Windows, window => window.Content is ChromeContent);
            Assert.Single(answers);
        }
        finally
        {
            hostWindow.Close();
            client.Destroy();
        }
    }
}
