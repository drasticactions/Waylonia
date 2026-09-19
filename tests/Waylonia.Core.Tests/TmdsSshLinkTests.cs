using System.Text;
using Basin.Diagnostics;
using Waylonia.Sessions;
using Xunit;

namespace Waylonia.Tests;

public sealed class TmdsSshLinkTests
{
    public const string DestinationVariable = "WAYLONIA_SSH_TEST";

    private static string? Destination => Environment.GetEnvironmentVariable(DestinationVariable);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(30);

    private sealed class EnvironmentPrompter : ISshPrompter
    {
        public bool AcceptHostKeys { get; init; } = true;

        public List<SshHostKeyPrompt> HostKeys { get; } = [];

        public Task<string?> AskSecretAsync(SshSecretPrompt prompt, CancellationToken cancellation) =>
            Task.FromResult<string?>(Environment.GetEnvironmentVariable("WAYLONIA_SSH_TEST_PASSWORD") ?? "not the password");

        public Task<bool> ConfirmHostKeyAsync(SshHostKeyPrompt prompt, CancellationToken cancellation)
        {
            HostKeys.Add(prompt);
            return Task.FromResult(AcceptHostKeys);
        }
    }

    private static async Task<(string Output, int Code)> RunToEndAsync(ISshLink link, string script, TimeSpan limit)
    {
        using var command = await link.RunAsync(script, Ct);
        using var reader = new StreamReader(command.Output, Encoding.UTF8);
        var text = await reader.ReadToEndAsync(Ct).WaitAsync(limit, Ct);
        return (text, await command.Exited.WaitAsync(limit, Ct));
    }

    [Fact]
    public async Task A_real_host_answers_a_forward_a_command_a_signal_and_a_loss()
    {
        Assert.SkipUnless(Destination is { Length: > 0 }, $"set {DestinationVariable} to an ssh destination to run this test");
        var factory = new TmdsSshLinkFactory(static () => TimeSpan.FromSeconds(30), BasinLog.For("test"), WayloniaPaths.Xdg());
        await using var link = factory.Create(Destination!, new EnvironmentPrompter());
        Assert.False(link.IsConnected);

        await link.ConnectAsync(Ct);
        Assert.True(link.IsConnected);
        Assert.False(link.Lost.IsCancellationRequested);

        var (hello, code) = await RunToEndAsync(link, "printf 'hello %s' waylonia; exit 3", TimeSpan.FromSeconds(30));
        Assert.Equal("hello waylonia", hello);
        Assert.Equal(3, code);

        using (var noisy = await link.RunAsync("echo one >&2; echo two >&2; printf out", Ct))
        {
            var lines = new List<string>();
            using var reader = new StreamReader(noisy.Output, Encoding.UTF8);
            var output = await reader.ReadToEndAsync(Ct);
            await foreach (var line in noisy.ErrorLines(Ct))
            {
                lines.Add(line);
            }

            Assert.Equal("out", output);
            Assert.Equal(["one", "two"], lines);
            Assert.Equal(0, await noisy.Exited);
        }

        var remotePath = $"/tmp/waylonia-test-{Environment.ProcessId}-{Guid.NewGuid():n}.sock";
        using (var listener = await link.ListenUnixAsync(remotePath, Ct))
        {
            var (tools, _) = await RunToEndAsync(
                link, "if command -v socat >/dev/null 2>&1; then echo socat; elif command -v nc >/dev/null 2>&1 && nc -h 2>&1 | grep -q -- -U; then echo nc; fi",
                TimeSpan.FromSeconds(30));
            var tool = tools.Trim();
            if (tool.Length > 0)
            {
                var connect = tool == "socat"
                    ? $"printf ping | socat - UNIX-CONNECT:{remotePath}"
                    : $"printf ping | nc -U {remotePath}";
                using var client = await link.RunAsync(connect, Ct);
                var accepted = await listener.AcceptAsync(Ct).AsTask().WaitAsync(Limit, Ct);
                Assert.NotNull(accepted);
                using (accepted)
                {
                    var buffer = new byte[4];
                    var read = 0;
                    while (read < 4)
                    {
                        var chunk = await accepted.ReadAsync(buffer.AsMemory(read), Ct).AsTask().WaitAsync(Limit, Ct);
                        Assert.NotEqual(0, chunk);
                        read += chunk;
                    }

                    Assert.Equal("ping", Encoding.ASCII.GetString(buffer));
                    await accepted.WriteAsync(Encoding.ASCII.GetBytes("pong"), Ct);
                    await accepted.FlushAsync(Ct);
                }

                using var clientOut = new StreamReader(client.Output, Encoding.UTF8);
                Assert.Equal("pong", await clientOut.ReadToEndAsync(Ct).WaitAsync(Limit, Ct));
            }

            listener.Dispose();
            Assert.Null(await listener.AcceptAsync(Ct));
        }

        await RunToEndAsync(link, $"rm -f {remotePath}", Limit);

        using (var sleeper = await link.RunAsync("trap 'exit 42' TERM; sleep 60 & wait $!", Ct))
        {
            await Task.Delay(500, Ct);
            Assert.True(sleeper.TrySignal("TERM"));
            var exit = await sleeper.Exited.WaitAsync(Limit, Ct);
            Assert.NotEqual(0, exit);
            Assert.False(sleeper.TrySignal("TERM"));
        }

        var lost = new TaskCompletionSource();
        link.Lost.Register(() => lost.TrySetResult());
        using var killer = await link.RunAsync("kill $PPID", Ct);
        await lost.Task.WaitAsync(Limit, Ct);
        Assert.False(link.IsConnected);
        Assert.Equal(-1, await killer.Exited.WaitAsync(Limit, Ct));
        var gone = await Assert.ThrowsAsync<SshLinkException>(() => link.RunAsync("true", Ct));
        Assert.Equal(SshLinkReason.Lost, gone.Reason);
    }

    [Fact]
    public async Task An_unreachable_host_and_a_refused_login_speak_in_sentences()
    {
        Assert.SkipUnless(Destination is { Length: > 0 }, $"set {DestinationVariable} to an ssh destination to run this test");
        var factory = new TmdsSshLinkFactory(static () => TimeSpan.FromSeconds(5), BasinLog.For("test"), WayloniaPaths.Xdg());

        await using var refused = factory.Create("127.0.0.1:1", new EnvironmentPrompter());
        var unreachable = await Assert.ThrowsAsync<SshLinkException>(() => refused.ConnectAsync(Ct));
        Assert.Equal(SshLinkReason.Unreachable, unreachable.Reason);
        Assert.StartsWith("127.0.0.1 could not be reached: ", unreachable.Message, StringComparison.Ordinal);

        var host = SshPromptText.HostOf(Destination!);
        await using var nobody = factory.Create($"waylonia-nobody-{Guid.NewGuid():n}@{host}", new EnvironmentPrompter());
        var denied = await Assert.ThrowsAsync<SshLinkException>(() => nobody.ConnectAsync(Ct));
        Assert.Equal(SshLinkReason.AuthenticationFailed, denied.Reason);
        Assert.Contains("refused every credential", denied.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_host_key_is_put_to_the_prompter_and_a_no_rejects_it()
    {
        Assert.SkipUnless(Destination is { Length: > 0 }, $"set {DestinationVariable} to an ssh destination to run this test");
        var host = SshPromptText.HostOf(Destination!);
        var addresses = await System.Net.Dns.GetHostAddressesAsync(host, System.Net.Sockets.AddressFamily.InterNetwork, Ct);
        Assert.SkipWhen(addresses.Length == 0, $"{host} has no IPv4 address to connect to by number");
        var user = Destination!.Contains('@') ? Destination[..Destination.IndexOf('@')] : Environment.UserName;
        var prompter = new EnvironmentPrompter { AcceptHostKeys = false };
        var factory = new TmdsSshLinkFactory(static () => TimeSpan.FromSeconds(30), BasinLog.For("test"), WayloniaPaths.Xdg());

        await using var link = factory.Create($"{user}@{addresses[0]}", prompter);
        try
        {
            await link.ConnectAsync(Ct);
            Assert.Skip($"{addresses[0]} is already in known_hosts, so nothing was asked");
        }
        catch (SshLinkException error)
        {
            Assert.Equal(SshLinkReason.HostKeyRejected, error.Reason);
            Assert.Equal($"the host key of {addresses[0]} was not accepted", error.Message);
        }

        var asked = Assert.Single(prompter.HostKeys);
        Assert.Equal(addresses[0].ToString(), asked.Host);
        Assert.Equal(22, asked.Port);
        Assert.Equal(SshHostKeyState.Unknown, asked.State);
        Assert.StartsWith("SHA256:", asked.Fingerprint, StringComparison.Ordinal);
        Assert.Contains(asked.KeyType, new[] { "ssh-ed25519", "ssh-rsa", "ecdsa-sha2-nistp256" });
    }
}
