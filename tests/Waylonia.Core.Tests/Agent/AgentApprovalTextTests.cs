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
        Assert.Equal("Start a program", prompt.Heading);
        Assert.Equal("xterm -hold", prompt.Subject);
        Assert.Equal("The agent wants to start a program that is not on this profile's allowlist.", prompt.Description);
        Assert.Equal("Yes, and don't ask again for xterm this run", prompt.DontAskAgain);
    }

    [Fact]
    public void Each_gated_call_reads_as_an_action()
    {
        Assert.Equal("Close window", AgentApprovalText.For("windows/close", "{}", Two).Heading);
        Assert.Equal("End a program", AgentApprovalText.For("process/kill", "{}", Two).Heading);
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
    [InlineData(120, "No answer in 2:00 means No.")]
    [InlineData(101.2, "No answer in 1:42 means No.")]
    [InlineData(0.4, "No answer in 0:01 means No.")]
    [InlineData(-3, "No answer in 0:00 means No.")]
    public void The_countdown_rounds_up_to_the_second(double seconds, string text) =>
        Assert.Equal(text, AgentApprovalText.Countdown(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void A_window_is_its_title_and_app_id()
    {
        Assert.Equal("da@mini-lab:~  (foot)", AgentApprovalText.Window("da@mini-lab:~", "foot"));
        Assert.Equal("foot", AgentApprovalText.Window("foot", "foot"));
    }
}
