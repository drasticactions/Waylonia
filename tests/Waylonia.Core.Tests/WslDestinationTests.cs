using Waylonia.Sessions;
using Xunit;

namespace Waylonia.Tests;

public sealed class WslDestinationTests
{
    [Theory]
    [InlineData("wsl:Ubuntu-26.04", "Ubuntu-26.04")]
    [InlineData("WSL:Debian", "Debian")]
    [InlineData("wsl:", "")]
    public void A_wsl_prefix_names_the_distribution(string destination, string distro)
    {
        Assert.True(WslDestination.TryParse(destination, out var parsed));
        Assert.Equal(distro, parsed);
    }

    [Theory]
    [InlineData("user@devbox")]
    [InlineData("wsl")]
    [InlineData("devbox:2222")]
    public void An_ssh_destination_is_not_a_wsl_one(string destination)
    {
        Assert.False(WslDestination.IsWsl(destination));
    }

    [Fact]
    public void A_distribution_name_is_letters_digits_dots_underscores_and_dashes()
    {
        Assert.Null(WslDestination.DistroProblem("Ubuntu-26.04"));
        Assert.Null(WslDestination.DistroProblem(""));
        Assert.NotNull(WslDestination.DistroProblem("my distro"));
        Assert.NotNull(WslDestination.DistroProblem("x;rm"));
    }

    [Fact]
    public void A_script_runs_through_exec_in_the_home_directory_and_reports_its_pid_first()
    {
        Assert.Equal(
            ["-d", "Ubuntu", "--cd", "~", "--exec", "sh", "-c", "echo \"waylonia-wsl-pid $$\" >&2; exec sh -c \"$1\"", "sh", "echo hi"],
            WslDestination.Script("Ubuntu", "echo hi"));
        Assert.Equal(["--cd", "~", "--exec", "wslinfo", "--vm-id"], WslDestination.VmId(""));
        Assert.Equal(["-d", "Ubuntu", "--cd", "~", "--exec", "kill", "-s", "TERM", "42"], WslDestination.Signal("Ubuntu", "TERM", 42));
    }

    [Fact]
    public void The_pid_line_is_recognized_and_other_lines_are_not()
    {
        Assert.Equal(314, WslDestination.ParsePid("waylonia-wsl-pid 314"));
        Assert.Null(WslDestination.ParsePid("314"));
        Assert.Null(WslDestination.ParsePid("See journalctl for more details."));
    }

    [Fact]
    public void The_distribution_list_survives_utf16_leftovers_and_carriage_returns()
    {
        Assert.Equal(["Ubuntu-26.04", "Debian"], WslDestination.ParseDistros("U\0b\0u\0n\0t\0u\0-\02\06\0.\00\04\0\r\n\0Debian\r\n\r\n"));
    }
}
