using Basin.Freedesktop;
using Waylonia.Agent;
using Xunit;

namespace Waylonia.Tests;

public sealed class AgentAllowlistTests
{
    private static readonly AgentAllowlist Allow = new(
    [
        AgentAllowlist.Parse("foot")!,
        AgentAllowlist.Parse("chromium --force-renderer-accessibility")!,
        AgentAllowlist.Parse("org.gnome.TextEditor.desktop")!,
        AgentAllowlist.Parse(["firefox", "--no-remote"])!,
    ]);

    private static DesktopEntry? Desktop(string id) => id == "org.gnome.TextEditor.desktop"
        ? new DesktopEntry { Id = id, Path = "/usr/share/applications/" + id, Name = "Text Editor", Exec = "gnome-text-editor %U" }
        : null;

    [Theory]
    [InlineData("foot", "foot")]
    [InlineData("/usr/bin/foot", "foot")]
    [InlineData("/tmp/elsewhere/foot", "foot")]
    [InlineData("chromium", "chromium --force-renderer-accessibility")]
    public void A_command_matches_by_the_basename_of_its_first_word(string command, string entry) =>
        Assert.Equal(entry, Allow.MatchCommand(command)?.ToString());

    [Theory]
    [InlineData("xterm")]
    [InlineData("foot.desktop")]
    [InlineData("")]
    public void Anything_else_matches_nothing(string command) => Assert.Null(Allow.MatchCommand(command));

    [Fact]
    public void A_desktop_id_matches_with_or_without_its_suffix()
    {
        Assert.NotNull(Allow.MatchDesktop("org.gnome.TextEditor"));
        Assert.NotNull(Allow.MatchDesktop("org.gnome.TextEditor.desktop"));
        Assert.Null(Allow.MatchDesktop("foot"));
    }

    [Fact]
    public void A_listed_command_runs_the_entry_program_with_its_own_arguments_first()
    {
        var plan = AgentLauncher.Plan(Allow, "/tmp/elsewhere/foot", ["-e", "top"], null, Desktop, out var error);
        Assert.Null(error);
        Assert.NotNull(plan);
        Assert.True(plan.Listed);
        Assert.Equal(["foot", "-e", "top"], plan.Argv);

        var chromium = AgentLauncher.Plan(Allow, "chromium", ["https://example.org"], null, Desktop, out _)!;
        Assert.Equal(["chromium", "--force-renderer-accessibility", "https://example.org"], chromium.Argv);
        Assert.Equal("chromium", AgentLauncher.Key(chromium));
    }

    [Fact]
    public void An_unlisted_command_is_planned_as_given_and_marked_unlisted()
    {
        var plan = AgentLauncher.Plan(Allow, "xterm", ["-hold"], null, Desktop, out _)!;
        Assert.False(plan.Listed);
        Assert.Equal(["xterm", "-hold"], plan.Argv);
        Assert.Equal("xterm", AgentLauncher.Key(plan));
    }

    [Fact]
    public void A_desktop_entry_runs_its_own_command_line()
    {
        var plan = AgentLauncher.Plan(Allow, null, [], "org.gnome.TextEditor", Desktop, out var error)!;
        Assert.Null(error);
        Assert.True(plan.Listed);
        Assert.Equal(["gnome-text-editor"], plan.Argv);
        Assert.Equal("org.gnome.TextEditor.desktop", AgentLauncher.Key(plan));
    }

    [Theory]
    [InlineData("foot", "org.gnome.TextEditor", "not both")]
    [InlineData(null, null, "not neither")]
    [InlineData("foot -e top", null, "has spaces")]
    [InlineData(null, "missing.app", "no .desktop entry")]
    [InlineData(null, "../etc/passwd", "not a .desktop id")]
    public void A_bad_request_says_why(string? command, string? desktopId, string why)
    {
        Assert.Null(AgentLauncher.Plan(Allow, command, [], desktopId, Desktop, out var error));
        Assert.Contains(why, error, StringComparison.Ordinal);
    }

    [Fact]
    public void Quoted_words_stay_together()
    {
        var entry = AgentAllowlist.Parse("\"my app\" --flag 'two words'")!;
        Assert.Equal("my app", entry.Program);
        Assert.Equal(["--flag", "two words"], entry.Arguments);
    }
}
