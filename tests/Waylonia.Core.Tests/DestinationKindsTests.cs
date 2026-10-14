using Waylonia.Sessions;
using Xunit;

namespace Waylonia.Tests;

public sealed class DestinationKindsTests
{
    [Theory]
    [InlineData("wsl:Ubuntu-26.04", "wsl", "Ubuntu-26.04")]
    [InlineData("WSL:Debian", "wsl", "Debian")]
    [InlineData("wsl:", "wsl", "")]
    [InlineData("distrobox:dev", "distrobox", "dev")]
    [InlineData("toolbox:fedora-toolbox-42", "toolbox", "fedora-toolbox-42")]
    [InlineData("incus:web", "incus", "web")]
    public void A_prefix_names_the_kind_and_the_target(string destination, string key, string name)
    {
        var kind = DestinationKinds.For(destination);

        Assert.Equal(key, kind.Key);
        Assert.True(kind.TryParse(destination, out var parsed));
        Assert.Equal(name, parsed);
        Assert.Equal(name, DestinationKinds.Name(destination));
    }

    [Theory]
    [InlineData("user@devbox")]
    [InlineData("wsl")]
    [InlineData("incus")]
    [InlineData("devbox:2222")]
    public void Anything_else_is_an_ssh_destination(string destination)
    {
        Assert.True(DestinationKinds.For(destination).IsSsh);
        Assert.Equal(destination, DestinationKinds.Name(destination));
    }

    [Fact]
    public void A_wsl_name_is_letters_digits_dots_underscores_and_dashes_and_may_be_empty()
    {
        Assert.Null(DestinationKinds.Wsl.NameProblem("Ubuntu-26.04"));
        Assert.Null(DestinationKinds.Wsl.NameProblem(""));
        Assert.NotNull(DestinationKinds.Wsl.NameProblem("my distro"));
        Assert.NotNull(DestinationKinds.Wsl.NameProblem("x;rm"));
    }

    [Fact]
    public void A_container_name_follows_podman_and_is_never_empty()
    {
        foreach (var kind in new[] { DestinationKinds.Distrobox, DestinationKinds.Toolbox })
        {
            Assert.Null(kind.NameProblem("fedora-toolbox-42"));
            Assert.Null(kind.NameProblem("0box_a.b"));
            Assert.NotNull(kind.NameProblem("-dev"));
            Assert.NotNull(kind.NameProblem(".dev"));
            Assert.NotNull(kind.NameProblem("my box"));
            Assert.Equal($"a {kind.Key} container needs a name; there is no default one", kind.NameProblem(""));
        }
    }

    [Fact]
    public void An_incus_name_starts_with_a_letter_holds_letters_digits_and_dashes_and_is_short()
    {
        var incus = DestinationKinds.Incus;
        Assert.Null(incus.NameProblem("web-1"));
        Assert.NotNull(incus.NameProblem("1web"));
        Assert.NotNull(incus.NameProblem("web_1"));
        Assert.NotNull(incus.NameProblem("web.1"));
        Assert.Null(incus.NameProblem(new string('a', 63)));
        Assert.NotNull(incus.NameProblem(new string('a', 64)));
        Assert.NotNull(incus.NameProblem(""));
    }

    [Fact]
    public void Each_kind_says_what_it_does_and_where_it_is_reached()
    {
        Assert.Equal(
            "ssh logs in to a host and distrobox enters a local container; a session takes one of them",
            DestinationKinds.BothProblem(DestinationKinds.Ssh, DestinationKinds.Distrobox));
        Assert.Equal(
            "wsl:Ubuntu names a WSL distribution, and only waylonia on Windows reaches one",
            DestinationKinds.Wsl.Unreachable("wsl:Ubuntu"));
        Assert.Equal(
            "distrobox:dev names a distrobox container, and only waylonia on Linux reaches one",
            DestinationKinds.Distrobox.Unreachable("distrobox:dev"));
    }

    [Fact]
    public void Only_distrobox_and_toolbox_share_home_and_refuse_a_desktop()
    {
        Assert.True(DestinationKinds.Distrobox.SharesHome);
        Assert.True(DestinationKinds.Toolbox.SharesHome);
        Assert.False(DestinationKinds.Incus.SharesHome);
        Assert.True(DestinationKinds.Incus.RunsDesktop);
        Assert.True(DestinationKinds.Ssh.RunsDesktop);
        Assert.StartsWith("a distrobox container shares this machine's session bus", DestinationKinds.Distrobox.DesktopProblem, StringComparison.Ordinal);
        Assert.True(DestinationKinds.Incus.TakesUser);
        Assert.False(DestinationKinds.Distrobox.TakesUser);
    }
}
