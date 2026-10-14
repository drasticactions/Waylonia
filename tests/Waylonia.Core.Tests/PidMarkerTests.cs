using Waylonia.Sessions;
using Xunit;

namespace Waylonia.Tests;

public sealed class PidMarkerTests
{
    [Fact]
    public void The_pid_line_is_recognized_and_other_lines_are_not()
    {
        Assert.Equal(314, PidMarker.Parse("waylonia-pid 314"));
        Assert.Null(PidMarker.Parse("314"));
        Assert.Null(PidMarker.Parse("waylonia-pid x"));
        Assert.Null(PidMarker.Parse("See journalctl for more details."));
    }

    [Fact]
    public void The_wrapper_prints_the_shell_pid_to_stderr_and_execs_its_first_argument()
    {
        Assert.Equal("echo \"waylonia-pid $$\" >&2; exec sh -c \"$1\"", PidMarker.Wrapper);
    }
}
