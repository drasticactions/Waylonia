using Basin.Diagnostics;
using Waylonia.Sessions;
using Xunit;

namespace Waylonia.Tests;

[Collection(LogCaptureCollection.Name)]
public sealed class RunRulesTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "waylonia-rules-" + Guid.NewGuid().ToString("n"));

    private Config Config()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "waylonia.toml");
        File.WriteAllText(path, string.Empty);
        return Waylonia.Config.Load(WayloniaPaths.ConfigOnly(path), BasinLogger.None);
    }

    [Fact]
    public void Autoconnect_follows_the_catalog_order_and_skips_what_is_already_starting()
    {
        var catalog = new SessionCatalog(
        [
            new SessionProfile("b", "user@b", Autoconnect: true),
            new SessionProfile("idle", "user@idle"),
            new SessionProfile("a", "user@a", Autoconnect: true),
        ], []);
        var config = Config();
        var already = SessionSettings.Resolve(new SessionProfile("a", "user@a-cli"), new SessionOverrides(), config, BasinLogger.None, adHoc: true).Settings!;

        var initial = RunRules.Autoconnect(catalog, [already], config, "f32", BasinLogger.None);

        Assert.Equal(["a", "b"], initial.Select(static settings => settings.Name));
        Assert.Same(already, initial[0]);
        Assert.Equal("f32", initial[1].AudioFormat);
    }

    [Fact]
    public void A_second_desktop_and_a_broken_profile_are_warned_about_and_skipped()
    {
        var catalog = new SessionCatalog(
        [
            new SessionProfile("one", "user@one", Autoconnect: true, Desktop: "sway"),
            new SessionProfile("two", "user@two", Autoconnect: true, Desktop: "niri"),
            new SessionProfile("bad", "user@bad", Autoconnect: true, Video: "mpeg"),
            new SessionProfile("plain", "user@plain", Autoconnect: true),
        ], []);
        using var capture = new LogCapture();

        var initial = RunRules.Autoconnect(catalog, [], Config(), "s16", BasinLog.For("test"));

        Assert.Equal(["one", "plain"], initial.Select(static settings => settings.Name));
        Assert.Equal(
        [
            "Warn: session two autoconnects a desktop and another desktop is already starting, skipping it",
            "Warn: session bad cannot autoconnect: video 'mpeg' is not a codec choice such as h264,hw",
        ], capture.Lines);
    }

    [Fact]
    public void The_nested_shell_refuses_a_desktop_and_a_listener()
    {
        Assert.Null(RunRules.NestedShellProblem(Waylonia.Shell.ShellMode.Windows, "sway", "/tmp/x"));
        Assert.Null(RunRules.NestedShellProblem(Waylonia.Shell.ShellMode.Nested, null, null));
        Assert.Equal(
            "--shell nested manages the windows itself and --desktop hands the screen to sway",
            RunRules.NestedShellProblem(Waylonia.Shell.ShellMode.Nested, "sway", null));
        Assert.Equal(
            "--shell nested manages the windows itself and --waypipe-listen hands them to whoever started the channel",
            RunRules.NestedShellProblem(Waylonia.Shell.ShellMode.Nested, null, "/tmp/x"));
    }

    [Theory]
    [InlineData("host", null, null, null, null, "--agent runs its own applications and --ssh connects a session")]
    [InlineData(null, "sway", null, null, null, "--agent runs its own applications and --desktop runs a whole desktop session")]
    [InlineData(null, null, "unix:/tmp/w", null, null, "--agent runs its own applications and --waypipe-listen waits for someone else's")]
    [InlineData(null, null, null, "foot", null, "--agent starts what the agent asks for through waylonia/launch and a trailing command starts a client of its own")]
    [InlineData(null, null, null, null, "windows", "--agent holds every window in one nested shell and --shell windows opens one host window per client")]
    public void Agent_mode_refuses_what_brings_in_other_clients(
        string? ssh, string? desktop, string? listen, string? command, string? shell, string problem) =>
        Assert.Equal(problem, RunRules.AgentProblem(Linux, ssh, desktop, listen, command, shell is null ? null : Waylonia.Shell.ShellModes.Parse(shell)));

    [Fact]
    public void Agent_mode_needs_local_commands_and_takes_the_nested_shell()
    {
        Assert.Null(RunRules.AgentProblem(Linux, null, null, null, null, null));
        Assert.Null(RunRules.AgentProblem(Linux, null, null, null, null, Waylonia.Shell.ShellMode.Nested));
        Assert.Contains("needs Linux", RunRules.AgentProblem(HostCapabilities.None, null, null, null, null, null), StringComparison.Ordinal);
    }

    private static readonly HostCapabilities Linux = HostCapabilities.None with { LocalCommands = true };

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

}
