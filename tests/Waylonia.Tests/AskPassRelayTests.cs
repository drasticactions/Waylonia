using Basin.Diagnostics;
using Waylonia.Ui;
using Xunit;

namespace Waylonia.Tests;

public sealed class AskPassRelayTests
{
    [Fact]
    public void Prompts_and_answers_round_trip_through_the_line_encoding()
    {
        var prompt = "user@devbox's password: ünïcode\nsecond line";
        Assert.Equal(prompt, AskPassRelay.Decode(AskPassRelay.Encode(prompt)));
        Assert.True(AskPassRelay.TryDecodeAnswer(AskPassRelay.EncodeAnswer("hunter2"), out var answer));
        Assert.Equal("hunter2", answer);
        Assert.True(AskPassRelay.TryDecodeAnswer(AskPassRelay.EncodeAnswer(null), out var cancelled));
        Assert.Null(cancelled);
        Assert.False(AskPassRelay.TryDecodeAnswer("garbage", out _));
        Assert.False(AskPassRelay.TryDecodeAnswer(null, out _));
    }

    [Fact]
    public void A_prompt_reaches_the_shell_and_the_answer_comes_back()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "unix sockets under the temp path are a Linux and macOS test");
        var path = Path.Combine(Path.GetTempPath(), $"waylonia-askpass-test-{Environment.ProcessId}");
        string? seen = null;
        using var server = AskPassServer.TryStart(path, prompt =>
        {
            seen = prompt;
            return Task.FromResult<string?>(prompt.EndsWith("(yes/no)", StringComparison.Ordinal) ? "yes" : "s3cret");
        }, BasinLogger.None);
        Assert.NotNull(server);

        var answer = AskPassRelay.Ask(path, "user@devbox's password:", TimeSpan.FromSeconds(5), out var relayed);
        Assert.True(relayed);
        Assert.Equal("s3cret", answer);
        Assert.Equal("user@devbox's password:", seen);

        var yes = AskPassRelay.Ask(path, "Continue (yes/no)", TimeSpan.FromSeconds(5), out relayed);
        Assert.True(relayed);
        Assert.Equal("yes", yes);
    }

    [Fact]
    public void A_cancelled_prompt_is_reported_as_relayed_with_no_answer()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "unix sockets under the temp path are a Linux and macOS test");
        var path = Path.Combine(Path.GetTempPath(), $"waylonia-askpass-cancel-{Environment.ProcessId}");
        using var server = AskPassServer.TryStart(path, _ => Task.FromResult<string?>(null), BasinLogger.None);
        Assert.NotNull(server);

        var answer = AskPassRelay.Ask(path, "password:", TimeSpan.FromSeconds(5), out var relayed);
        Assert.True(relayed);
        Assert.Null(answer);
    }

    [Fact]
    public void Without_a_listener_the_relay_reports_nothing_so_the_dialog_falls_back()
    {
        var answer = AskPassRelay.Ask(Path.Combine(Path.GetTempPath(), "waylonia-askpass-nobody"), "password:", TimeSpan.FromSeconds(1), out var relayed);
        Assert.False(relayed);
        Assert.Null(answer);
    }

    [Fact]
    public void Configure_names_the_relay_socket_only_when_given_one()
    {
        var environment = new Dictionary<string, string?>();
        AskPass.Configure(environment, "/run/user/1000/waylonia-askpass-1");
        Assert.Equal("/run/user/1000/waylonia-askpass-1", environment[AskPassRelay.SocketVariable]);
        AskPass.Configure(environment);
        Assert.False(environment.ContainsKey(AskPassRelay.SocketVariable));
    }
}
