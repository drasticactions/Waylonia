using System.Text;
using System.Text.Json;
using Basin.Shell.Nested;
using Waylonia.Agent;
using Xunit;

namespace Waylonia.Tests;

public sealed class AgentAuditFormatTests
{
    private static readonly DateTimeOffset Time = new(2026, 9, 23, 14, 2, 11, 412, TimeSpan.Zero);

    [Fact]
    public void A_call_is_one_json_line_in_the_documented_shape()
    {
        var line = AgentAuditFormat.Call(
            Time, "input/pointer-button", """{"window":7,"x":120,"y":44,"button":"left"}"""u8, keepText: false, null, 18.04,
            "000041-before.png", "000041-after.png", null);
        Assert.Equal(
            """{"t":"2026-09-23T14:02:11.412Z","method":"input/pointer-button","params":{"window":7,"x":120,"y":44,"button":"left"},"ok":true,"ms":18,"before":"000041-before.png","after":"000041-after.png","approval":null}""",
            line);
        Assert.DoesNotContain('\n', line);
    }

    [Theory]
    [InlineData("input/text")]
    [InlineData("clipboard/write")]
    public void Typed_and_copied_text_is_logged_as_its_length_and_hash(string method)
    {
        var line = AgentAuditFormat.Call(Time, method, """{"text":"hunter2","kind":"clipboard"}"""u8, keepText: false, null, 1, null, null, null);
        using var document = JsonDocument.Parse(line);
        var text = document.RootElement.GetProperty("params").GetProperty("text");
        Assert.Equal(7, text.GetProperty("length").GetInt32());
        Assert.Equal(AgentAuditFormat.Hash("hunter2"), text.GetProperty("sha256").GetString());
        Assert.Equal("clipboard", document.RootElement.GetProperty("params").GetProperty("kind").GetString());
        Assert.DoesNotContain("hunter2", line, StringComparison.Ordinal);

        var kept = AgentAuditFormat.Call(Time, method, """{"text":"hunter2"}"""u8, keepText: true, null, 1, null, null, null);
        Assert.Contains("hunter2", kept, StringComparison.Ordinal);
    }

    [Fact]
    public void The_hash_is_the_sha256_of_the_utf8_text() =>
        Assert.Equal(
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes("é"))),
            AgentAuditFormat.Hash("é"));

    [Fact]
    public void A_failed_call_carries_its_error_and_an_approval()
    {
        var line = AgentAuditFormat.Call(Time, "waylonia/launch", """{"command":"xterm"}"""u8, false, "refused: no", 2.26, null, null, "deny");
        using var document = JsonDocument.Parse(line);
        Assert.False(document.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("refused: no", document.RootElement.GetProperty("error").GetString());
        Assert.Equal("deny", document.RootElement.GetProperty("approval").GetString());
        Assert.Equal(2.3, document.RootElement.GetProperty("ms").GetDouble());
    }

    [Fact]
    public void Events_and_file_names()
    {
        Assert.Equal(
            """{"t":"2026-09-23T14:02:11.412Z","event":"takeover","state":"paused"}""",
            AgentAuditFormat.Event(Time, "takeover", [("state", "paused")]));
        Assert.Equal("2026-09-23-140211-4242.jsonl", AgentAuditFormat.FileName(Time, 4242));
    }

    [Fact]
    public void The_agent_shell_is_one_workspace_with_no_panel_and_no_keys()
    {
        var configured = new ShellSettings(Workspaces: 4, Keys: [new ShellKey("close", "Alt+F4")]);
        var settings = AgentShell.Settings(new AgentProfile("a", "/a"), configured);
        Assert.Equal(1, settings.Workspaces);
        Assert.Empty(settings.Keys);
        Assert.Equal(PlacementMode.Maximize, settings.Placement);
        Assert.Equal(configured.Theme, settings.Theme);
        Assert.Equal(new PanelLayout(0, 0, 0), AgentShell.Panels);
        Assert.Empty(AgentShell.Arrangement.Top);
        Assert.Empty(AgentShell.Arrangement.Bottom);
    }
}
