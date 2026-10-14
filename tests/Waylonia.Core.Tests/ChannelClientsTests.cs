using Basin;
using Basin.Hosted;
using Basin.Transport.Waypipe;
using Wayland.Server;
using Waylonia.Sessions;
using Xunit;

namespace Waylonia.Tests;

public sealed class ChannelClientsTests
{
    private sealed class Owner(string name, LinuxDmabufGlobal dmabuf) : IChannelOwner
    {
        public string Name => name;

        public WaypipeGlobals Globals { get; } = new(carriesDmabuf: true);

        public SshSession? Session => null;

        public int Attached => 0;

        public bool Carries(string interfaceName) => Globals.Carries(interfaceName);

        public bool ExclusiveDmabuf(WlGlobal global) => dmabuf.Owns(global);

        public bool SeesDmabuf(WlGlobal global) => dmabuf.Owns(global);
    }

    [Fact]
    public void Each_channel_client_sees_only_its_own_dmabuf_global_and_a_local_client_sees_neither()
    {
        CompositorHarness.SkipWithoutWaylandClient();
        var map = new ChannelClients();
        Owner? first = null;
        Owner? second = null;
        using var harness = new CompositorHarness(
            configure: host =>
            {
                host.Display.SetGlobalFilter(map.Filter);
                first = new Owner("first", Dmabuf(host));
                second = new Owner("second", Dmabuf(host));
            },
            onClient: client => map.Add(client, first!));
        var other = harness.AddClient(client => map.Add(client, second!));
        var local = harness.AddClient();

        var firstSeen = DmabufNames(harness.Client);
        var secondSeen = DmabufNames(other);
        var localSeen = DmabufNames(local);

        Assert.Single(firstSeen);
        Assert.Single(secondSeen);
        Assert.NotEqual(firstSeen[0], secondSeen[0]);
        Assert.DoesNotContain(firstSeen[0], localSeen);
        Assert.DoesNotContain(secondSeen[0], localSeen);
        Assert.Equal(2, map.Count);
    }

    [Fact]
    public void A_direct_client_sees_every_global_and_the_host_dmabuf_which_stays_visible_to_local_clients()
    {
        CompositorHarness.SkipWithoutWaylandClient();
        var map = new ChannelClients();
        Owner? channel = null;
        LinuxDmabufGlobal? hostDmabuf = null;
        DirectOwner? direct = null;
        using var harness = new CompositorHarness(
            configure: host =>
            {
                host.Display.SetGlobalFilter(map.Filter);
                channel = new Owner("channel", Dmabuf(host));
                hostDmabuf = Dmabuf(host);
                direct = new DirectOwner("box", null, global => hostDmabuf.Owns(global));
            },
            onClient: client => map.Add(client, channel!));
        var boxed = harness.AddClient(client => map.Add(client, direct!));
        var local = harness.AddClient();

        var channelSeen = DmabufNames(harness.Client);
        var boxedSeen = DmabufNames(boxed);
        var localSeen = DmabufNames(local);

        Assert.Single(boxedSeen);
        Assert.Equal(boxedSeen, localSeen);
        Assert.DoesNotContain(channelSeen[0], boxedSeen);
        var hidden = new WaypipeGlobals(carriesDmabuf: true);
        var refused = local.Globals.Select(entry => entry.Interface).Where(name => !hidden.Carries(name)).ToList();
        Assert.NotEmpty(refused);
        Assert.All(refused, name => Assert.Contains(boxed.Globals, entry => entry.Interface == name));
        Assert.All(refused, name => Assert.DoesNotContain(harness.Client.Globals, entry => entry.Interface == name));
    }

    private static LinuxDmabufGlobal Dmabuf(BasinCompositorHost host) => new(
        host.Display,
        host.Services.Require<ClientBufferRegistry>(),
        WaypipeGlobals.ChannelFormats,
        WaypipeGlobals.SyntheticMainDevice,
        compositor: host.Services.Require<CompositorGlobal>());

    private static List<uint> DmabufNames(ShmTestClient client) =>
        client.Globals.Where(entry => entry.Interface == "zwp_linux_dmabuf_v1").Select(entry => entry.Name).ToList();
}
