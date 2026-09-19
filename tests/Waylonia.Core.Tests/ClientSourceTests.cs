using Waylonia;
using Xunit;

namespace Waylonia.Tests;

public sealed class ClientSourceTests
{
    private static readonly HostCapabilities Linux = HostCapabilities.None with { LocalCommands = true, LocalDesktops = true, ChannelsOnly = false };

    private static readonly HostCapabilities Elsewhere = HostCapabilities.None;

    [Fact]
    public void A_bare_run_is_never_missing_a_client_source_because_the_manager_is_one()
    {
        Assert.Null(RunRules.LocalCommandProblem(Linux, command: null));
        Assert.Null(RunRules.LocalCommandProblem(Elsewhere, command: null));
    }

    [Fact]
    public void A_local_command_needs_a_host_that_runs_local_clients()
    {
        Assert.Null(RunRules.LocalCommandProblem(Linux, command: "foot"));
        Assert.NotNull(RunRules.LocalCommandProblem(Elsewhere, command: "foot"));
    }

    [Fact]
    public void A_local_desktop_needs_a_host_that_runs_local_desktops()
    {
        Assert.Null(RunRules.LocalDesktopProblem(Linux));
        Assert.NotNull(RunRules.LocalDesktopProblem(Elsewhere));
    }
}
