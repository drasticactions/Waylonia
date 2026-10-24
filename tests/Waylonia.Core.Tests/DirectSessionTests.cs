using Waylonia.Sessions;
using Waylonia.Tests.Ssh;
using Xunit;

namespace Waylonia.Tests;

[Collection(LogCaptureCollection.Name)]
[System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
public sealed class DirectSessionTests : IDisposable
{
    private const string LoggedIn = "waylonia: logged in\n";

    private readonly string _directory = Directory.CreateTempSubdirectory("waylonia-direct-").FullName;

    private static async Task WaitUntilAsync(Func<bool> condition, string what)
    {
        for (var i = 0; i < 500 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition(), what);
    }

    private (StubSessionHost Host, FakeSshLinkFactory Links) Host(DestinationKind kind, HostSettings? settings = null)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "a direct display is a unix socket with a file mode");
        var links = new FakeSshLinkFactory { Shape = ChannelShape.Direct, DisplayDirectory = _directory };
        var host = new StubSessionHost { Settings = settings ?? new HostSettings(TrayApps: false), XDisplay = ":3" };
        host.Targets[kind] = links;
        return (host, links);
    }

    private static async Task<(SshSession Session, FakeSshLink Link, FakeSshCommand Master)> ConnectedAsync(
        StubSessionHost host, FakeSshLinkFactory links, SessionSettings settings)
    {
        var session = new SshSession(settings, host);
        var connecting = session.ConnectAsync();
        await WaitUntilAsync(() => links.Last?.Commands.Count > 0, "the session script was started");
        var link = links.Last!;
        var master = link.Command(0);
        await master.WriteOutputAsync(LoggedIn);
        Assert.True(await connecting);
        return (session, link, master);
    }

    [Fact]
    public async Task A_direct_link_binds_a_display_the_host_adopts_and_runs_a_master_without_waypipe()
    {
        var (host, links) = Host(DestinationKinds.Incus);
        var settings = StubSessionHost.For("web") with { Ssh = "incus:web" };

        var (session, link, master) = await ConnectedAsync(host, links, settings);

        Assert.Equal(["connect", $"display {session.RemoteDisplay}", "run"], link.Events);
        Assert.Equal(session.DirectScript(), master.Script);
        Assert.DoesNotContain("waypipe", master.Script, StringComparison.Ordinal);
        Assert.StartsWith("echo 'waylonia: logged in'; ", master.Script, StringComparison.Ordinal);
        Assert.Contains("cat >/dev/null; status=$?; for g in /tmp/waylonia-groups-", master.Script, StringComparison.Ordinal);
        Assert.Contains("kill -s KILL -- \"-${g##*/}\"", master.Script, StringComparison.Ordinal);
        var (fd, owner) = Assert.Single(host.Adopted);
        Assert.Equal(link.Display!.ListenFd, fd);
        Assert.Same(owner, session.ChannelOwner);
        Assert.Same(session, owner.Session);
        Assert.Equal("web", owner.Name);
        Assert.True(owner.Carries("zwp_virtual_keyboard_manager_v1"));
        Assert.Equal(new DisplayRequest(session.RemoteDisplay, ":3", false), link.DisplayRequest);
        Assert.Equal(SessionStatus.Connected, session.Status);
        Assert.True(File.Exists(link.Display.Path));
        await session.DisconnectAsync();
    }

    [Fact]
    public async Task A_launch_names_the_display_socket_and_the_kind_display()
    {
        var (host, links) = Host(DestinationKinds.Distrobox);
        var (session, link, _) = await ConnectedAsync(host, links, StubSessionHost.For("dev") with { Ssh = "distrobox:dev" });

        Assert.True(await session.LaunchAsync("foot", "test"));

        var script = link.Command(1).Script;
        Assert.Equal(session.ClientScript("foot"), script);
        Assert.Contains("DISPLAY=:3; export DISPLAY; ", script, StringComparison.Ordinal);
        Assert.Contains($"XDG_SESSION_TYPE=wayland WAYLAND_DISPLAY={session.RemoteDisplay} sh -c 'foot'; ", script, StringComparison.Ordinal);
        Assert.StartsWith("mkdir -p /tmp/waylonia-groups-", script, StringComparison.Ordinal);
        Assert.EndsWith("s=$?; rm -f /tmp/waylonia-groups-" + session.RemoteDisplay["waylonia-".Length..] + "/$$; exit $s", script, StringComparison.Ordinal);
        Assert.DoesNotContain("while [ ! -S", script, StringComparison.Ordinal);
        await session.DisconnectAsync();
    }

    [Fact]
    public async Task Without_xwayland_a_launch_unsets_display()
    {
        var (host, links) = Host(DestinationKinds.Toolbox);
        host.XDisplay = null;
        var (session, link, _) = await ConnectedAsync(host, links, StubSessionHost.For("tb") with { Ssh = "toolbox:tb" });

        Assert.True(await session.LaunchAsync("xterm", "test"));

        Assert.Contains("unset DISPLAY; ", link.Command(1).Script, StringComparison.Ordinal);
        await session.DisconnectAsync();
    }

    [Fact]
    public async Task The_command_finishing_ends_an_ad_hoc_session_and_releases_the_display()
    {
        var (host, links) = Host(DestinationKinds.Distrobox);
        var settings = StubSessionHost.For("dev", adHoc: true) with { Ssh = "distrobox:dev", Command = "foot" };
        var session = new SshSession(settings, host);
        var ended = new List<int>();
        session.Ended += (_, code) => ended.Add(code);
        var connecting = session.ConnectAsync();
        await WaitUntilAsync(() => links.Last?.Commands.Count > 0, "the session script was started");
        var link = links.Last!;
        await link.Command(0).WriteOutputAsync(LoggedIn);
        Assert.True(await connecting);
        await WaitUntilAsync(() => link.Commands.Count > 1, "the command was started");
        var socket = link.Display!.Path;

        link.Command(1).Exit(0);

        await WaitUntilAsync(() => ended.Count > 0, "the session ended");
        Assert.Equal([0], ended);
        Assert.True(session.CommandFinished);
        Assert.Equal(SessionStatus.Disconnected, session.Status);
        Assert.Equal(1, host.Released);
        Assert.False(File.Exists(socket));
        Assert.Null(session.Display);
        Assert.True(link.Disposed);
    }

    [Fact]
    public async Task Disconnecting_releases_the_adoption_and_removes_the_socket()
    {
        var (host, links) = Host(DestinationKinds.Incus);
        var (session, link, _) = await ConnectedAsync(host, links, StubSessionHost.For("web") with { Ssh = "incus:web" });
        var socket = link.Display!.Path;

        await session.DisconnectAsync();

        Assert.Equal(1, host.Released);
        Assert.False(File.Exists(socket));
        Assert.NotEmpty(host.Posted);
        Assert.Equal(SessionStatus.Disconnected, session.Status);
    }

    [Fact]
    public async Task A_distrobox_first_start_outlasts_the_script_limit_and_says_so()
    {
        var (host, links) = Host(DestinationKinds.Distrobox);
        var session = new SshSession(StubSessionHost.For("dev") with { Ssh = "distrobox:wl-box" }, host)
        {
            ScriptStartLimit = TimeSpan.FromMilliseconds(50),
        };

        var connecting = session.ConnectAsync();
        await WaitUntilAsync(() => session.StatusText.Contains("first start", StringComparison.Ordinal), "the first start was reported");
        Assert.Contains("wl-box", session.StatusText, StringComparison.Ordinal);
        Assert.False(connecting.IsCompleted);

        await links.Last!.Command(0).WriteOutputAsync(LoggedIn);

        Assert.True(await connecting);
        await session.DisconnectAsync();
    }

    [Fact]
    public async Task A_first_start_can_be_canceled()
    {
        var (host, links) = Host(DestinationKinds.Toolbox);
        var session = new SshSession(StubSessionHost.For("tb") with { Ssh = "toolbox:tb" }, host)
        {
            ScriptStartLimit = TimeSpan.FromMilliseconds(50),
        };

        var connecting = session.ConnectAsync();
        await WaitUntilAsync(() => session.StatusText.Contains("first start", StringComparison.Ordinal), "the first start was reported");
        await session.DisconnectAsync();

        Assert.False(await connecting);
        Assert.Equal(SessionStatus.Disconnected, session.Status);
    }

    [Fact]
    public async Task Audio_in_a_container_that_shares_home_is_ignored_and_loads_no_sink()
    {
        var (host, links) = Host(DestinationKinds.Distrobox);
        using var capture = new LogCapture();
        var (session, link, master) = await ConnectedAsync(host, links, StubSessionHost.For("dev") with { Ssh = "distrobox:dev", Audio = true });

        Assert.DoesNotContain("pactl", master.Script, StringComparison.Ordinal);
        Assert.True(await session.LaunchAsync("foot", "test"));
        Assert.DoesNotContain("PULSE_SINK", link.Command(1).Script, StringComparison.Ordinal);
        Assert.Contains(capture.Lines, line => line.Contains("audio = true ignored", StringComparison.Ordinal));
        Assert.Equal(0, host.Audio.Count);
        await session.DisconnectAsync();
    }

    [Fact]
    public async Task An_incus_container_keeps_its_audio_through_the_master()
    {
        var (host, links) = Host(DestinationKinds.Incus);
        var (session, _, master) = await ConnectedAsync(host, links, StubSessionHost.For("web") with { Ssh = "incus:web", Audio = true });

        Assert.Contains("pactl load-module module-null-sink", master.Script, StringComparison.Ordinal);
        await session.DisconnectAsync();
    }

    [Fact]
    public async Task The_user_reaches_the_link_factory()
    {
        var (host, links) = Host(DestinationKinds.Incus);
        var (session, _, _) = await ConnectedAsync(host, links, StubSessionHost.For("web") with { Ssh = "incus:web", User = "ubuntu" });

        Assert.Equal("ubuntu", links.LastUser);
        await session.DisconnectAsync();
    }

    [Fact]
    public async Task A_container_kind_without_a_registered_link_fails_with_a_sentence()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "a direct display is a unix socket with a file mode");
        var host = new StubSessionHost { Settings = new HostSettings(TrayApps: false) };
        var session = new SshSession(StubSessionHost.For("dev") with { Ssh = "distrobox:dev" }, host);

        Assert.False(await session.ConnectAsync());

        Assert.Equal(DestinationKinds.Distrobox.Unreachable("distrobox:dev"), session.LastError);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
