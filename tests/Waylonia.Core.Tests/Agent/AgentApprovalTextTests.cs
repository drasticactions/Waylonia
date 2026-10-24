using Waylonia.Agent;
using Xunit;

namespace Waylonia.Tests;

public sealed class AgentApprovalTextTests
{
    private static readonly TimeSpan Two = TimeSpan.FromMinutes(2);

    [Fact]
    public void A_launch_names_the_command_line_and_what_yes_for_the_run_covers()
    {
        var prompt = AgentApprovalText.For("waylonia/launch", """{"command":"xterm"}""", Two, commandLine: "xterm -hold", commandKey: "xterm");
        Assert.Contains("program", prompt.Heading, StringComparison.Ordinal);
        Assert.Equal("xterm -hold", prompt.Subject);
        Assert.Contains("start", prompt.Description, StringComparison.Ordinal);
        Assert.Contains("xterm", prompt.DontAskAgain, StringComparison.Ordinal);
    }

    [Fact]
    public void Each_gated_call_reads_as_an_action()
    {
        Assert.Contains("window", AgentApprovalText.For("windows/close", "{}", Two).Heading, StringComparison.Ordinal);
        Assert.Contains("program", AgentApprovalText.For("process/kill", "{}", Two).Heading, StringComparison.Ordinal);
        Assert.Equal("The clipboard", AgentApprovalText.For("clipboard/read", "{}", Two).Subject);
        Assert.Equal("The primary selection", AgentApprovalText.For("clipboard/read", """{"kind":"primary"}""", Two).Subject);
        Assert.Equal("\"hello\"", AgentApprovalText.For("clipboard/write", """{"text":"hello"}""", Two).Subject);
        var unknown = AgentApprovalText.For("windows/move", """{"id":1}""", Two);
        Assert.Equal("windows/move", unknown.Heading);
        Assert.Null(unknown.Subject);
        Assert.Equal("windows/move\n{\"id\":1}", AgentApprovalText.Details(unknown));
    }

    [Fact]
    public void Long_clipboard_text_is_cut_and_counted()
    {
        var text = new string('a', 100) + "\nb";
        var subject = AgentApprovalText.For("clipboard/write", $$"""{"text":"{{text.Replace("\n", "\\n", StringComparison.Ordinal)}}"}""", Two).Subject;
        Assert.Equal($"\"{new string('a', 80)}…\" (102 characters)", subject);
    }

    [Theory]
    [InlineData(120, "2:00")]
    [InlineData(101.2, "1:42")]
    [InlineData(0.4, "0:01")]
    [InlineData(-3, "0:00")]
    public void The_countdown_rounds_up_to_the_second(double seconds, string time) =>
        Assert.Contains($" {time} ", AgentApprovalText.Countdown(TimeSpan.FromSeconds(seconds)), StringComparison.Ordinal);

    [Fact]
    public void A_window_is_its_title_and_app_id()
    {
        Assert.Equal("da@mini-lab:~  (foot)", AgentApprovalText.Window("da@mini-lab:~", "foot"));
        Assert.Equal("foot", AgentApprovalText.Window("foot", "foot"));
    }
}
