using Avalonia.Headless.XUnit;
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
        Assert.Equal("1", environment[AskPass.Variable]);
        Assert.Contains(environment["SSH_ASKPASS_REQUIRE"], new[] { "force", "prefer" });
        Assert.Equal("waylonia:0", environment["DISPLAY"]);
    }

    [Fact]
    public void A_display_the_host_already_has_is_kept()
    {
        var environment = new Dictionary<string, string?> { ["DISPLAY"] = ":1" };

        AskPass.Configure(environment);

        Assert.Equal(":1", environment["DISPLAY"]);
    }

    [Fact]
    public void Ssh_starts_the_askpass_run_with_only_the_prompt_as_arguments()
    {
        Assert.True(AskPass.IsAskPassRun(["Enter passphrase for key '/home/da/.ssh/id_rsa':"], "1"));
        Assert.True(AskPass.IsAskPassRun(["--askpass", "Password:"], null));
        Assert.False(AskPass.IsAskPassRun(["--ssh", "Test"], null));
        Assert.Equal("Enter passphrase for key:", AskPass.PromptOf(["Enter", "passphrase", "for", "key:"]));
        Assert.Equal("Password:", AskPass.PromptOf(["--askpass", "Password:"]));
        Assert.Equal("Password:", AskPass.PromptOf([]));
    }

    [AvaloniaFact]
    public void The_view_answers_yes_no_and_a_password_and_cancels_once()
    {
        var yesNo = new AskPassView("Continue (yes/no)", AskPassKind.YesNo);
        string? answer = null;
        var answers = 0;
        yesNo.Answered += value =>
        {
            answer = value;
            answers++;
        };
        yesNo.Finish("yes");
        yesNo.Finish("no");
        Assert.Equal("yes", answer);
        Assert.Equal(1, answers);

        var password = new AskPassView("password:", AskPassKind.Password);
        string? secret = "unset";
        password.Answered += value => secret = value;
        password.Finish(null);
        Assert.Null(secret);
    }
}
