using System.Text;
using Waylonia.Sessions;
using Waylonia.Tests.Ssh;
using Xunit;

namespace Waylonia.Tests;

public sealed class WslLinkTests
{
    public const string DistroVariable = "WAYLONIA_WSL_TEST";

    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(30);

    private static string? Distro => Environment.GetEnvironmentVariable(DistroVariable);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<(string Output, int Code)> RunToEndAsync(ISshLink link, string script)
    {
        using var command = await link.RunAsync(script, Ct);
        using var reader = new StreamReader(command.Output, Encoding.UTF8);
        var text = await reader.ReadToEndAsync(Ct).WaitAsync(Limit, Ct);
        return (text, await command.Exited.WaitAsync(Limit, Ct));
    }

    [Fact]
    public async Task A_real_distribution_runs_commands_takes_a_signal_and_reaches_the_listener()
    {
        Assert.SkipUnless(Distro is not null, $"set {DistroVariable} to a WSL distribution with python3 (\"\" for the default) to run this test");
        var factory = new WslLinkFactory();
        if (Distro!.Length > 0)
        {
            Assert.Contains(Distro, await factory.ListAsync(Ct));
        }

        await using var link = factory.Create(DestinationKinds.Wsl.For(Distro), new FakePrompter());
        await link.ConnectAsync(Ct);
        Assert.True(link.IsConnected);

        var (hello, code) = await RunToEndAsync(link, "printf 'hello %s' \"$(printf waylonia)\"; exit 3");
        Assert.Equal("hello waylonia", hello);
        Assert.Equal(3, code);

        using (var noisy = await link.RunAsync("echo one >&2; echo two >&2", Ct))
        {
            var lines = new List<string>();
            await foreach (var line in noisy.ErrorLines(Ct))
            {
                lines.Add(line);
            }

            Assert.Contains("one", lines);
            Assert.Contains("two", lines);
            Assert.DoesNotContain(lines, line => line.StartsWith(PidMarker.Prefix, StringComparison.Ordinal));
        }

        using (var sleeper = await link.RunAsync("sleep 60", Ct))
        {
            var deadline = DateTime.UtcNow + Limit;
            while (!sleeper.TrySignal("TERM") && DateTime.UtcNow < deadline)
            {
                await Task.Delay(50, Ct);
            }

            Assert.NotEqual(0, await sleeper.Exited.WaitAsync(Limit, Ct));
        }

        using var listener = await link.ListenAsync("/tmp/unused.sock", Ct);
        var port = listener.WaypipeArgument["--vsock -s ".Length..];
        var accepting = listener.AcceptAsync(Ct).AsTask();
        var (_, sent) = await RunToEndAsync(
            link,
            $"python3 -c 'import socket; s = socket.socket(socket.AF_VSOCK, socket.SOCK_STREAM); s.connect((2, {port})); s.sendall(b\"hi\")'");
        Assert.Equal(0, sent);
        await using var stream = await accepting.WaitAsync(Limit, Ct);
        Assert.NotNull(stream);
        var buffer = new byte[2];
        await stream.ReadExactlyAsync(buffer, Ct);
        Assert.Equal("hi", Encoding.ASCII.GetString(buffer));
    }
}
