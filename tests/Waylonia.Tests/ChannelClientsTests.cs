using Avalonia.Headless.XUnit;
using Basin;
using Basin.Avalonia;
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

        public bool OwnsDmabuf(WlGlobal global) => dmabuf.Owns(global);
    }

    [AvaloniaFact]
    public void Each_channel_client_sees_only_its_own_dmabuf_global_and_a_local_client_sees_neither()
    {
        WayloniaHostHarness.SkipWithoutWaylandClient();
        var map = new ChannelClients();
        Owner? first = null;
        Owner? second = null;
        using var harness = new WayloniaHostHarness(
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

    private static LinuxDmabufGlobal Dmabuf(BasinCompositorHost host) => new(
        host.Display,
        host.Services.Require<ClientBufferRegistry>(),
        WaypipeGlobals.ChannelFormats,
        WaypipeGlobals.SyntheticMainDevice,
        compositor: host.Services.Require<CompositorGlobal>());

    private static List<uint> DmabufNames(ShmTestClient client) =>
        client.Globals.Where(entry => entry.Interface == "zwp_linux_dmabuf_v1").Select(entry => entry.Name).ToList();
}
