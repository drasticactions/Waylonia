using Waylonia;
using Xunit;

namespace Waylonia.Tests;

public sealed class ClientSourceTests
{
    [Fact]
    public void A_bare_run_is_never_missing_a_client_source_because_the_manager_is_one()
    {
        Assert.Null(Program.LocalCommandProblem(linux: true, command: null));
        Assert.Null(Program.LocalCommandProblem(linux: false, command: null));
    }

    [Fact]
    public void A_local_command_needs_linux()
    {
        Assert.Null(Program.LocalCommandProblem(linux: true, command: "foot"));
        Assert.NotNull(Program.LocalCommandProblem(linux: false, command: "foot"));
    }
}
