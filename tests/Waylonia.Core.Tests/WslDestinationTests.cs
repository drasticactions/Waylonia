using Waylonia.Sessions;
using Xunit;

namespace Waylonia.Tests;

public sealed class WslDestinationTests
{
    [Fact]
    public void A_script_runs_through_exec_in_the_home_directory_and_reports_its_pid_first()
    {
        Assert.Equal(
            ["-d", "Ubuntu", "--cd", "~", "--exec", "sh", "-c", "echo \"waylonia-pid $$\" >&2; exec sh -c \"$1\"", "sh", "echo hi"],
            WslDestination.Script("Ubuntu", "echo hi"));
        Assert.Equal(["--cd", "~", "--exec", "wslinfo", "--vm-id"], WslDestination.VmId(""));
        Assert.Equal(["-d", "Ubuntu", "--cd", "~", "--exec", "kill", "-s", "TERM", "42"], WslDestination.Signal("Ubuntu", "TERM", 42));
    }

    [Fact]
    public void The_distribution_list_survives_utf16_leftovers_and_carriage_returns()
    {
        Assert.Equal(["Ubuntu-26.04", "Debian"], WslDestination.ParseDistros("U\0b\0u\0n\0t\0u\0-\02\06\0.\00\04\0\r\n\0Debian\r\n\r\n"));
    }
}
