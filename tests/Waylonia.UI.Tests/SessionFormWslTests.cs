using Waylonia.Sessions;
using Waylonia.UI;
using Xunit;

namespace Waylonia.Tests;

public sealed class SessionFormWslTests
{
    [Fact]
    public void A_wsl_profile_loads_as_the_wsl_kind_with_its_distribution()
    {
        var form = new SessionFormViewModel();

        form.Load(new SessionProfile("ubuntu", "wsl:Ubuntu-26.04"));

        Assert.True(form.IsWsl);
        Assert.True(form.ShowsKind);
        Assert.Equal("Ubuntu-26.04", form.Distro);
        Assert.Equal(string.Empty, form.Ssh);
    }

    [Fact]
    public void The_wsl_kind_saves_the_distribution_and_an_empty_one_means_the_default()
    {
        var form = new SessionFormViewModel { WslAvailable = true };
        form.Load(new SessionProfile("ubuntu", string.Empty));
        form.KindIndex = SessionFormViewModel.WslKind;
        form.Distro = " Ubuntu ";

        Assert.Equal("wsl:Ubuntu", form.Validate()!.Ssh);

        form.Distro = string.Empty;
        Assert.Equal("wsl:", form.Validate()!.Ssh);

        form.Distro = "my distro";
        Assert.Null(form.Validate());
        Assert.NotNull(form.SshProblem);
    }

    [Fact]
    public void The_kind_picker_shows_only_where_wsl_is_reachable_and_ssh_refuses_a_wsl_destination()
    {
        var form = new SessionFormViewModel();
        form.Load(new SessionProfile("dev", "user@dev"));
        Assert.False(form.ShowsKind);

        form.Ssh = "wsl:Ubuntu";
        Assert.Null(form.Validate());
        Assert.Equal("Choose WSL as the kind to start a WSL distribution.", form.SshProblem);
    }
}
