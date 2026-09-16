using Waylonia.Sessions;
using Waylonia.Tests.Ssh;
using Xunit;

namespace Waylonia.Tests;

public sealed class SshSessionTests
{
    private const string LoggedIn = "waylonia: logged in\n";

    private static async Task WaitUntilAsync(Func<bool> condition, string what)
    {
        for (var i = 0; i < 500 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition(), what);
    }

    private static async Task<(SshSession Session, FakeSshLink Link, FakeSshCommand Master)> ConnectedAsync(StubSessionHost host, SessionSettings? settings = null)
    {
        var session = new SshSession(settings ?? StubSessionHost.For("dev"), host);
        var connecting = session.ConnectAsync();
        await WaitUntilAsync(() => host.Links.Last?.Commands.Count > 0, "the session script was started");
        var link = host.Links.Last!;
        var master = link.Command(0);
        await master.WriteOutputAsync(LoggedIn);
        Assert.True(await connecting);
        return (session, link, master);
    }

    [Fact]
    public async Task The_forward_is_listened_before_the_script_runs_and_the_marker_completes_the_login()
    {
        var host = new StubSessionHost { Settings = new HostSettings(TrayApps: false) };
        var session = new SshSession(StubSessionHost.For("dev"), host);

        var connecting = session.ConnectAsync();
        await WaitUntilAsync(() => host.Links.Last?.Commands.Count > 0, "the session script was started");
        var link = host.Links.Last!;
        Assert.Equal(["connect", $"listen {session.RemoteSocket}", "run"], link.Events);
        Assert.Equal(session.RemoteScript(), link.Command(0).Script);
        Assert.Equal(SessionStatus.Connecting, session.Status);
        Assert.False(connecting.IsCompleted);

        await link.Command(0).WriteOutputAsync("some banner\n" + LoggedIn);
        Assert.True(await connecting);
        Assert.Equal(SessionStatus.Connected, session.Status);
        Assert.Same(host.Prompter, ((FakeSshLink)host.Links.Links[0]).Prompter);
        Assert.Contains("some banner", session.RecentOutput);
    }

    [Fact]
    public async Task A_script_that_exits_before_the_marker_fails_the_login_with_its_last_line()
    {
        var host = new StubSessionHost { Settings = new HostSettings(TrayApps: false) };
        var session = new SshSession(StubSessionHost.For("dev"), host);
        var ended = new List<int>();
        session.Ended += (_, code) => ended.Add(code);

        var connecting = session.ConnectAsync();
        await WaitUntilAsync(() => host.Links.Last?.Commands.Count > 0, "the session script was started");
        var master = host.Links.Last!.Command(0);
        master.WriteError("sh: waypipe: not found");
        master.Exit(127);

        Assert.False(await connecting);
        Assert.Equal("the session script on user@dev exited with 127: sh: waypipe: not found", session.LastError);
        Assert.Equal(SessionStatus.Disconnected, session.Status);
        Assert.Equal([1], ended);
        Assert.True(host.Links.Last!.Disposed);
    }

    [Fact]
    public async Task A_script_that_never_prints_the_marker_times_out()
    {
        var host = new StubSessionHost { Settings = new HostSettings(TrayApps: false) };
        var session = new SshSession(StubSessionHost.For("dev"), host) { ScriptStartLimit = TimeSpan.FromMilliseconds(200) };

        Assert.False(await session.ConnectAsync());
        Assert.Equal("the session script on user@dev did not start within 0 seconds", session.LastError);
        Assert.Equal(SessionStatus.Disconnected, session.Status);
    }

    [Fact]
    public async Task A_failed_login_shows_the_sentence_and_ends_the_session()
    {
        var host = new StubSessionHost { Settings = new HostSettings(TrayApps: false) };
        host.Links.ConnectOutcomes.Enqueue(new SshLinkException(
            SshLinkReason.AuthenticationFailed, SshLinkException.Sentence(SshLinkReason.AuthenticationFailed, "user@dev", "publickey")));
        var session = new SshSession(StubSessionHost.For("dev"), host);
        var ended = new List<int>();
        session.Ended += (_, code) => ended.Add(code);

        Assert.False(await session.ConnectAsync());
        Assert.Equal("user@dev refused every credential (publickey)", session.LastError);
        Assert.Equal(SessionStatus.Disconnected, session.Status);
        Assert.Equal([1], ended);
        Assert.Equal(["connect", "dispose"], host.Links.Last!.Events);
    }

    [Fact]
    public async Task A_cancelled_prompt_ends_the_login_quietly()
    {
        var host = new StubSessionHost { Settings = new HostSettings(TrayApps: false) };
        host.Links.BeforeConnect = async link =>
        {
            var answer = await link.Prompter.AskSecretAsync(new SshSecretPrompt(SshSecretKind.Password, link.Destination, 1), CancellationToken.None);
            if (answer is null)
            {
                link.ConnectOutcome = new SshLinkException(SshLinkReason.Cancelled, SshLinkException.Sentence(SshLinkReason.Cancelled, link.Destination));
            }
        };
        host.Prompter.SecretAnswer = null;
        using var capture = new LogCapture();
        var session = new SshSession(StubSessionHost.For("dev"), host);

        Assert.False(await session.ConnectAsync());
        Assert.Equal("the login to user@dev was cancelled", session.LastError);
        Assert.Single(host.Prompter.Secrets);
        Assert.DoesNotContain(capture.Lines, line => line.Contains("cancelled", StringComparison.Ordinal) && line.Contains("error", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_channel_stream_becomes_a_waypipe_channel()
    {
        var host = new StubSessionHost { Settings = new HostSettings(TrayApps: false) };
        var (session, link, _) = await ConnectedAsync(host);
        Assert.False(session.HadClients);

        using var channel = new DuplexStream();
        link.Listener!.Inject(channel);
        await WaitUntilAsync(() => session.HadClients, "the stream was adopted");

        Assert.Equal(1, session.Acceptor!.Attached);
        Assert.Single(host.Posted);
        session.Dispose();
    }

    [Fact]
    public async Task Losing_the_link_ends_the_session_with_the_connection_sentence()
    {
        var host = new StubSessionHost { Settings = new HostSettings(TrayApps: false) };
        var (session, link, _) = await ConnectedAsync(host);
        var ended = new List<int>();
        session.Ended += (_, code) => ended.Add(code);

        link.Drop();
        await WaitUntilAsync(() => ended.Count > 0, "the session noticed the loss");

        Assert.Equal("the connection to user@dev ended", session.LastError);
        Assert.Equal([1], ended);
        Assert.True(link.Disposed);
    }

    [Fact]
    public async Task The_script_ending_cleanly_ends_the_session_with_zero()
    {
        var host = new StubSessionHost { Settings = new HostSettings(TrayApps: false) };
        var (session, _, master) = await ConnectedAsync(host);
        var ended = new List<int>();
        session.Ended += (_, code) => ended.Add(code);

        master.Exit(0);
        await WaitUntilAsync(() => ended.Count > 0, "the session noticed the script end");

        Assert.Equal([0], ended);
    }

    [Fact]
    public async Task The_script_failing_reports_its_code_and_last_error_line()
    {
        var host = new StubSessionHost { Settings = new HostSettings(TrayApps: false) };
        var (session, _, master) = await ConnectedAsync(host);
        var ended = new List<int>();
        session.Ended += (_, code) => ended.Add(code);

        master.WriteError("waypipe: socket in use");
        await WaitUntilAsync(() => session.RecentOutput.Contains("waypipe: socket in use"), "stderr reached the tail");
        master.Exit(3);
        await WaitUntilAsync(() => ended.Count > 0, "the session noticed the script end");

        Assert.Equal("the session script on user@dev exited with 3: waypipe: socket in use", session.LastError);
        Assert.Equal([1], ended);
    }

    [Fact]
    public async Task Launching_after_a_loss_reconnects_first()
    {
        var host = new StubSessionHost { Settings = new HostSettings(TrayApps: false) };
        var (session, first, _) = await ConnectedAsync(host);
        first.Drop();
        await WaitUntilAsync(() => session.Status == SessionStatus.Disconnected, "the session noticed the loss");

        var launching = session.LaunchAsync("foot", "hotkey");
        await WaitUntilAsync(() => host.Links.Links.Count == 2 && host.Links.Last!.Commands.Count > 0, "a second link ran the script");
        var second = host.Links.Last!;
        await second.Command(0).WriteOutputAsync(LoggedIn);

        Assert.True(await launching);
        Assert.Equal(2, second.Commands.Count);
        Assert.Equal(session.ClientScript("foot"), second.Command(1).Script);
        Assert.Contains("WAYLAND_DISPLAY=" + session.RemoteDisplay, second.Command(1).Script);
    }

    [Fact]
    public async Task Stopping_clients_signals_then_disposes_each_launched_command()
    {
        var host = new StubSessionHost { Settings = new HostSettings(TrayApps: false) };
        var (session, link, _) = await ConnectedAsync(host);
        Assert.True(await session.LaunchAsync("foot", "hotkey"));
        Assert.True(await session.LaunchAsync("firefox", "hotkey"));
        var clients = link.Commands.Skip(1).ToList();
        Assert.Equal(2, clients.Count);

        Assert.True(session.StopClients());
        Assert.All(clients, client =>
        {
            Assert.Equal(["TERM"], client.Signals);
            Assert.True(client.Disposed);
        });
        Assert.False(session.StopClients());
    }

    [Fact]
    public async Task Disconnecting_removes_the_remote_socket_before_the_link_goes()
    {
        var host = new StubSessionHost { Settings = new HostSettings(TrayApps: false) };
        var (session, link, master) = await ConnectedAsync(host);

        var disconnecting = session.DisconnectAsync();
        await WaitUntilAsync(() => link.Commands.Count == 2, "the removal script ran");
        var removal = link.Command(1);
        Assert.Equal(session.RemoveSocketScript(), removal.Script);
        Assert.False(link.Disposed);
        removal.Exit(0);
        await disconnecting;

        Assert.Equal(["connect", $"listen {session.RemoteSocket}", "run", "run", "dispose"], link.Events);
        Assert.True(link.Listener!.Disposed);
        Assert.True(master.Disposed);
        Assert.Equal(SessionStatus.Disconnected, session.Status);
        Assert.Equal("disconnected from user@dev", session.StatusText);
    }

    [Fact]
    public async Task Recent_output_is_the_stderr_tail_of_the_script()
    {
        var host = new StubSessionHost { Settings = new HostSettings(TrayApps: false) };
        var (session, _, master) = await ConnectedAsync(host);
        for (var i = 0; i < 50; i++)
        {
            master.WriteError($"line {i}");
        }

        await WaitUntilAsync(() => session.RecentOutput.Count == OutputTail.DefaultCapacity && session.RecentOutput[^1] == "line 49", "the tail filled");
        Assert.Equal("line 10", session.RecentOutput[0]);
    }
}
