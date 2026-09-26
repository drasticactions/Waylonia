using Basin.Diagnostics;
using Basin.Shell.Nested;
using Tomlyn;
using Waylonia.Agent;
using Xunit;

namespace Waylonia.Tests;

[Collection(LogCaptureCollection.Name)]
public sealed class AgentProfileTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"waylonia-agent-tests-{Guid.NewGuid():N}");

    private static AgentProfile Parse(string toml) =>
        AgentProfileStore.Parse("test", "/profiles/test", Toml.ToModel(toml), BasinLogger.None);

    [Fact]
    public void The_example_profile_parses_every_key()
    {
        var profile = Parse("""
            size = "1024x640"
            scale = 1.5
            headless = true
            placement = "automatic"
            takeover-timeout = 0
            keymap = "de"

            [launch]
            allow = ["foot", "gnome-calculator", "org.gnome.TextEditor.desktop", ["chromium", "--force-renderer-accessibility"]]
            ask = false

            [approve]
            actions = ["launch-unlisted", "clipboard-read", "close-window"]

            [audit]
            screenshots = "all"
            text = true

            [accessibility]
            enabled = false
            """);

        Assert.Equal((1024, 640), (profile.Width, profile.Height));
        Assert.Equal(1.5, profile.Scale);
        Assert.True(profile.Headless);
        Assert.Equal(PlacementMode.Automatic, profile.Placement);
        Assert.Equal(0, profile.TakeoverTimeout);
        Assert.Equal("de", profile.Keymap);
        Assert.Equal(["foot", "gnome-calculator", "org.gnome.TextEditor.desktop", "chromium --force-renderer-accessibility"],
            profile.Allow.Entries.Select(static entry => entry.ToString()));
        Assert.False(profile.Ask);
        Assert.Equal(["launch-unlisted", "clipboard-read", "close-window"], profile.Approve);
        Assert.Equal(AgentScreenshots.All, profile.Screenshots);
        Assert.True(profile.AuditText);
        Assert.False(profile.Accessibility);
    }

    [Fact]
    public void Defaults_hold_where_a_value_is_missing_or_wrong()
    {
        using var capture = new LogCapture();
        var profile = AgentProfileStore.Parse("test", "/p", Toml.ToModel("""
            size = "huge"
            scale = 9.0
            placement = "sideways"

            [approve]
            actions = ["launch-unlisted", "format-disk"]

            [audit]
            screenshots = "sometimes"
            """), WayloniaLog.Log);

        Assert.Equal((AgentProfile.DefaultWidth, AgentProfile.DefaultHeight), (profile.Width, profile.Height));
        Assert.Equal(1.0, profile.Scale);
        Assert.Equal(PlacementMode.Maximize, profile.Placement);
        Assert.Equal(["launch-unlisted"], profile.Approve);
        Assert.Equal(AgentScreenshots.Acting, profile.Screenshots);
        Assert.True(profile.Ask);
        Assert.True(profile.Accessibility);
        Assert.Contains(capture.Lines, line => line.Contains("format-disk", StringComparison.Ordinal));
        Assert.Contains(capture.Lines, line => line.Contains("size takes WxH", StringComparison.Ordinal));
    }

    [Fact]
    public void A_missing_profile_is_created_with_a_placeholder_that_launches_nothing_without_asking()
    {
        var profile = AgentProfileStore.Load(_root, "fresh", BasinLogger.None, out var error);

        Assert.Null(error);
        Assert.NotNull(profile);
        Assert.True(File.Exists(profile.File));
        Assert.Contains("It is not a sandbox", File.ReadAllText(profile.File), StringComparison.Ordinal);
        foreach (var directory in new[] { profile.Home, profile.Logs, profile.Audit, Path.Combine(profile.Home, ".config"), Path.Combine(profile.Home, ".cache") })
        {
            Assert.True(Directory.Exists(directory), directory);
        }

        Assert.True(profile.Allow.IsEmpty);
        Assert.True(profile.Ask);
        Assert.True(profile.CanLaunchAnything);
        Assert.Contains("approval", profile.LaunchSummary, StringComparison.Ordinal);

        var locked = profile with { Ask = false };
        Assert.False(locked.CanLaunchAnything);
        Assert.Contains("launch nothing", locked.LaunchSummary, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("../escape")]
    [InlineData(".hidden")]
    [InlineData("has space")]
    public void A_profile_name_is_a_plain_file_name(string name)
    {
        Assert.Null(AgentProfileStore.Load(_root, name, BasinLogger.None, out var error));
        Assert.Contains("not a profile name", error, StringComparison.Ordinal);
    }

    [Fact]
    public void One_run_holds_a_profile_at_a_time()
    {
        var profile = AgentProfileStore.Load(_root, "locked", BasinLogger.None, out _)!;
        using var first = AgentProfileStore.Lock(profile, out var firstError);
        Assert.NotNull(first);
        Assert.Null(firstError);

        Assert.Null(AgentProfileStore.Lock(profile, out var secondError));
        Assert.Contains("another waylonia --agent locked is running", secondError, StringComparison.Ordinal);

        first.Dispose();
        using var third = AgentProfileStore.Lock(profile, out _);
        Assert.NotNull(third);
    }

    [Fact]
    public void Launch_unlisted_is_gated_by_ask_or_by_the_approve_list()
    {
        var asking = new AgentProfile("a", "/a");
        Assert.True(asking.Gates(AgentApprovals.LaunchUnlisted));
        Assert.False(asking.Gates(AgentApprovals.ClipboardRead));

        var strict = asking with { Ask = false };
        Assert.False(strict.Gates(AgentApprovals.LaunchUnlisted));
        Assert.True((strict with { Approve = [AgentApprovals.LaunchUnlisted] }).Gates(AgentApprovals.LaunchUnlisted));
        Assert.True((strict with { Approve = [AgentApprovals.Kill] }).Gates(AgentApprovals.Kill));
    }

    [Fact]
    public void Every_approval_action_names_a_method_and_back()
    {
        foreach (var action in AgentApprovals.Names)
        {
            var method = AgentApprovals.MethodOf(action);
            Assert.NotNull(method);
            Assert.Equal(action, AgentApprovals.ActionOf(method));
            Assert.StartsWith("the agent wants", AgentApprovals.Reason(action), StringComparison.Ordinal);
        }

        Assert.Null(AgentApprovals.ActionOf("windows/list"));
    }

    [Fact]
    public void The_profile_root_is_beside_the_shell_state()
    {
        var paths = WayloniaPaths.Xdg() with { StateFile = "/state/waylonia/shell.toml" };
        Assert.Equal("/state/waylonia/agents", AgentProfileStore.RootFor(paths));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
