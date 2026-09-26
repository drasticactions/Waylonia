using Basin.Hosted;
using Waylonia.Agent;
using Xunit;

namespace Waylonia.Tests;

public sealed class AgentTakeoverTests
{
    [Fact]
    public void A_press_pauses_and_motion_does_not()
    {
        var takeover = new AgentTakeover(10);
        var changes = 0;
        takeover.Changed += () => changes++;
        Assert.False(takeover.HostInput(1000, pressOrKey: false));
        Assert.Equal("Agent: driving", takeover.Status);

        Assert.True(takeover.HostInput(1000, pressOrKey: true));
        Assert.True(takeover.IsPaused);
        Assert.Equal("Agent: paused", takeover.Status);
        Assert.False(takeover.HostInput(1500, pressOrKey: true));
        Assert.Equal(1, changes);
    }

    [Fact]
    public void The_timeout_counts_from_the_last_host_input()
    {
        var takeover = new AgentTakeover(10);
        takeover.HostInput(1000, true);
        takeover.HostInput(5000, true);
        Assert.Equal(15000, takeover.ResumesAt);
        Assert.False(takeover.Tick(14999));
        Assert.True(takeover.Tick(15000));
        Assert.False(takeover.IsPaused);
        Assert.Null(takeover.ResumesAt);
    }

    [Fact]
    public void A_zero_timeout_leaves_only_the_button()
    {
        var takeover = new AgentTakeover(0);
        takeover.HostInput(0, true);
        Assert.Null(takeover.ResumesAt);
        Assert.False(takeover.Tick(long.MaxValue));
        Assert.True(takeover.Resume());
        Assert.False(takeover.Resume());
    }

    [Fact]
    public void A_pending_approval_wins_the_status()
    {
        var takeover = new AgentTakeover(10);
        var changes = 0;
        takeover.Changed += () => changes++;
        takeover.PendingApprovals = 1;
        Assert.Equal("Agent: waiting for approval", takeover.Status);
        takeover.HostInput(0, true);
        Assert.Equal("Agent: paused, waiting for approval", takeover.Status);
        takeover.Resume();
        takeover.PendingApprovals = 0;
        Assert.Equal("Agent: driving", takeover.Status);
        Assert.Equal(4, changes);
    }

    [Theory]
    [InlineData("input/pointer-button", true)]
    [InlineData("input/text", true)]
    [InlineData("waylonia/a11y-click", true)]
    [InlineData("waylonia/a11y-action", false)]
    [InlineData("capture/output", false)]
    [InlineData("windows/list", false)]
    public void A_pause_refuses_input_only(string method, bool refused) =>
        Assert.Equal(refused, AgentTakeover.Refuses(method));

    [Theory]
    [InlineData(BasinViewInputKind.PointerButton, true, true)]
    [InlineData(BasinViewInputKind.PointerButton, false, false)]
    [InlineData(BasinViewInputKind.Key, true, true)]
    [InlineData(BasinViewInputKind.Key, false, false)]
    [InlineData(BasinViewInputKind.TouchDown, true, true)]
    [InlineData(BasinViewInputKind.PointerAxis, false, true)]
    [InlineData(BasinViewInputKind.PointerMotion, false, false)]
    [InlineData(BasinViewInputKind.PointerEnter, false, false)]
    [InlineData(BasinViewInputKind.FocusIn, false, false)]
    public void Host_input_that_takes_over(BasinViewInputKind kind, bool pressed, bool takes) =>
        Assert.Equal(takes, AgentShell.TakesOver(new BasinViewInput(kind, 0, 0, 0, 0, pressed, 0, 0, 0)));
}
