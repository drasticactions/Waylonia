using Basin.Freedesktop;
using Waylonia.Agent;
using Xunit;

namespace Waylonia.Tests;

public sealed class AgentLauncherTests
{
    private static DesktopEntry? Desktop(string id) => id == "org.gnome.TextEditor.desktop"
        ? new DesktopEntry { Id = id, Path = "/usr/share/applications/" + id, Name = "Text Editor", Exec = "gnome-text-editor %U" }
        : null;

    [Fact]
    public void A_command_runs_as_given_with_its_arguments()
    {
        var plan = AgentLauncher.Plan("/usr/bin/xterm", ["-hold"], null, Desktop, out var error);
        Assert.Null(error);
        Assert.NotNull(plan);
        Assert.Equal(["/usr/bin/xterm", "-hold"], plan.Argv);
        Assert.Equal("xterm", plan.Name);
        Assert.Equal("xterm", AgentLauncher.Key(plan));
    }

    [Fact]
    public void A_desktop_entry_runs_its_own_command_line()
    {
        var plan = AgentLauncher.Plan(null, [], "org.gnome.TextEditor", Desktop, out var error)!;
        Assert.Null(error);
        Assert.Equal(["gnome-text-editor"], plan.Argv);
        Assert.Equal("org.gnome.TextEditor.desktop", AgentLauncher.Key(plan));
    }

    [Theory]
    [InlineData("foot", "org.gnome.TextEditor", "exactly one")]
    [InlineData(null, null, "exactly one")]
    [InlineData("foot -e top", null, "has spaces")]
    [InlineData(null, "missing.app", "No .desktop entry")]
    [InlineData(null, "../etc/passwd", "not a .desktop id")]
    public void A_bad_request_says_why(string? command, string? desktopId, string why)
    {
        Assert.Null(AgentLauncher.Plan(command, [], desktopId, Desktop, out var error));
        Assert.Contains(why, error, StringComparison.Ordinal);
    }
}
