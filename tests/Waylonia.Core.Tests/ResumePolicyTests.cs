using Waylonia.Sessions;
using Xunit;

namespace Waylonia.Tests;

public sealed class ResumePolicyTests
{
    private static readonly SessionCatalog Catalog = new(
    [
        new SessionProfile("auto", "user@auto", Autoconnect: true),
        new SessionProfile("manual", "user@manual"),
        new SessionProfile("desk", "user@desk", Autoconnect: true, Desktop: "sway"),
    ], []);

    [Fact]
    public void A_live_autoconnect_session_is_planned_and_a_manual_or_idle_one_is_not()
    {
        Assert.Equal(["auto", "desk"], ResumePolicy.Plan(["auto", "manual", "desk"], Catalog));
        Assert.Empty(ResumePolicy.Plan(["manual"], Catalog));
        Assert.Empty(ResumePolicy.Plan([], Catalog));
    }

    [Fact]
    public void An_ad_hoc_session_with_no_profile_is_not_planned_and_a_name_is_planned_once()
    {
        Assert.Equal(["auto"], ResumePolicy.Plan(["adhoc", "auto", "auto"], Catalog));
    }
}
