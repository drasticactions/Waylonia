using Waylonia.Ui;
using Xunit;

namespace Waylonia.Tests;

public sealed class AskPassTests
{
    [Theory]
    [InlineData("user@host's password: ")]
    [InlineData("Enter passphrase for key '/home/me/.ssh/id_ed25519': ")]
    [InlineData("Password:")]
    public void A_prompt_that_wants_a_secret_gets_a_password_box(string prompt)
    {
        Assert.Equal(AskPassKind.Password, AskPass.Classify(prompt));
    }

    [Theory]
    [InlineData("Are you sure you want to continue connecting (yes/no)? ")]
    [InlineData("Are you sure you want to continue connecting (yes/no/[fingerprint])? ")]
    [InlineData("Continue (YES/NO)?")]
    public void A_yes_no_prompt_gets_a_yes_no_pair(string prompt)
    {
        Assert.Equal(AskPassKind.YesNo, AskPass.Classify(prompt));
    }

    [Fact]
    public void Askpass_is_forced_only_without_a_terminal()
    {
        Assert.Equal("force", AskPass.Require(inputRedirected: true));
        Assert.Equal("prefer", AskPass.Require(inputRedirected: false));
    }

    [Fact]
    public void The_environment_points_ssh_at_this_executable()
    {
        var environment = new Dictionary<string, string?>();

        AskPass.Configure(environment);

        Assert.Equal(Environment.ProcessPath, environment["SSH_ASKPASS"]);
        Assert.Contains(environment["SSH_ASKPASS_REQUIRE"], new[] { "force", "prefer" });
        Assert.Equal(OperatingSystem.IsLinux(), environment.ContainsKey("DISPLAY"));
    }
}
