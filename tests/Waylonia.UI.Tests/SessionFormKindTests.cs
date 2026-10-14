using Waylonia.Sessions;
using Waylonia.UI;
using Xunit;

namespace Waylonia.Tests;

public sealed class SessionFormKindTests
{
    [Fact]
    public void A_wsl_profile_loads_as_the_wsl_kind_with_its_distribution()
    {
        var form = new SessionFormViewModel();

        form.Load(new SessionProfile("ubuntu", "wsl:Ubuntu-26.04"));

        Assert.Equal(DestinationKinds.Wsl, form.Kind);
        Assert.True(form.IsTarget);
        Assert.True(form.ShowsKind);
        Assert.Equal("Ubuntu-26.04", form.Target);
        Assert.Equal(string.Empty, form.Ssh);
    }

    [Fact]
    public void The_wsl_kind_saves_the_distribution_and_an_empty_one_means_the_default()
    {
        var form = new SessionFormViewModel { AvailableKinds = [DestinationKinds.Wsl] };
        form.Load(new SessionProfile("ubuntu", string.Empty));
        form.Kind = DestinationKinds.Wsl;
        form.Target = " Ubuntu ";

        Assert.Equal("wsl:Ubuntu", form.Validate()!.Ssh);

        form.Target = string.Empty;
        Assert.Equal("wsl:", form.Validate()!.Ssh);

        form.Target = "my distro";
        Assert.Null(form.Validate());
        Assert.NotNull(form.SshProblem);
    }

    [Fact]
    public void The_kind_picker_shows_only_where_another_kind_is_reachable_and_ssh_refuses_a_prefixed_destination()
    {
        var form = new SessionFormViewModel();
        form.Load(new SessionProfile("dev", "user@dev"));
        Assert.False(form.ShowsKind);

        form.Ssh = "wsl:Ubuntu";
        Assert.Null(form.Validate());
        Assert.Equal("Choose WSL as the kind to reach a WSL distribution.", form.SshProblem);

        form.Ssh = "incus:web";
        Assert.Null(form.Validate());
        Assert.Equal("Choose Incus as the kind to reach an Incus instance.", form.SshProblem);
    }

    [Fact]
    public void The_linux_kinds_are_offered_in_order_and_a_loaded_kind_stays_even_when_unregistered()
    {
        var form = new SessionFormViewModel { AvailableKinds = [DestinationKinds.Incus, DestinationKinds.Distrobox] };

        Assert.Equal([DestinationKinds.Ssh, DestinationKinds.Distrobox, DestinationKinds.Incus], form.Kinds);

        form.Load(new SessionProfile("tb", "toolbox:fedora"));

        Assert.Equal([DestinationKinds.Ssh, DestinationKinds.Distrobox, DestinationKinds.Toolbox, DestinationKinds.Incus], form.Kinds);
        Assert.Equal(DestinationKinds.Toolbox, form.Kind);
        Assert.Equal("fedora", form.Target);
    }

    [Fact]
    public void A_container_kind_lists_its_targets_and_refuses_an_empty_name()
    {
        var form = new SessionFormViewModel { AvailableKinds = [DestinationKinds.Distrobox, DestinationKinds.Incus] };
        form.SetListed(DestinationKinds.Distrobox, ["wl-box", "dev"]);
        form.SetListed(DestinationKinds.Incus, ["wl-test"]);
        form.Load(new SessionProfile("box", string.Empty));

        form.Kind = DestinationKinds.Distrobox;
        Assert.Equal(["wl-box", "dev"], form.Targets);
        Assert.Equal("container", form.TargetCaption);
        Assert.Null(form.Validate());
        Assert.Equal("Use the container name distrobox list shows.", form.SshProblem);

        form.Target = "wl-box";
        Assert.Equal("distrobox:wl-box", form.Validate()!.Ssh);

        form.Kind = DestinationKinds.Incus;
        Assert.Equal(["wl-test"], form.Targets);
        Assert.Equal("instance", form.TargetCaption);
    }

    [Fact]
    public void Only_an_incus_session_takes_a_user()
    {
        var form = new SessionFormViewModel { AvailableKinds = [DestinationKinds.Incus] };
        form.Load(new SessionProfile("web", "incus:web", User: "ubuntu"));
        Assert.True(form.ShowsUser);
        Assert.Equal("ubuntu", form.User);
        Assert.Equal("ubuntu", form.Validate()!.User);

        form.User = "-x";
        Assert.Null(form.Validate());
        Assert.NotNull(form.UserProblem);

        form.Kind = DestinationKinds.Ssh;
        form.Ssh = "user@web";
        Assert.False(form.ShowsUser);
        Assert.Null(form.Validate()!.User);
    }
}
