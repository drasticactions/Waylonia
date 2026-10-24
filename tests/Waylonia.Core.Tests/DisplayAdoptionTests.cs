using Waylonia.Sessions;
using Xunit;

namespace Waylonia.Tests;

[System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
public sealed class DisplayAdoptionTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("waylonia-adopt-").FullName;

    [Fact]
    public void A_client_on_an_adopted_socket_is_tagged_before_it_binds_and_its_window_carries_the_session_name()
    {
        CompositorHarness.SkipWithoutWaylandClient();
        var map = new ChannelClients();
        var session = new SshSession(StubSessionHost.For("box") with { Ssh = "distrobox:box" }, new StubSessionHost());
        var owner = new DirectOwner("box", session, _ => false);
        var taggedAtCreation = false;
        using var harness = new CompositorHarness(
            configure: host => host.Display.SetGlobalFilter(map.Filter),
            nested: new CompositorHarness.NestedShellOptions());
        harness.Shell!.WindowSuffix = client => map.OwnerOf(client) is { Session: not null } found ? found.Name : null;
        using var display = UnixSessionDisplay.Bind(
            Path.Combine(_directory, "wayland-0"), UnixFileMode.UserRead | UnixFileMode.UserWrite, "wayland-0", null);
        var adoption = DisplayAdoption.Start(
            harness.Host.Loop,
            harness.Host.Display,
            DisplayAdoption.Duplicate(display.ListenFd),
            client =>
            {
                owner.Adopted(client);
                map.Add(client, owner);
                taggedAtCreation = map.OwnerOf(client) is not null;
            });

        var boxed = harness.ConnectTo(display.Path, () => owner.Attached > 0);
        harness.MapToplevel(title: "foot", client: boxed);

        Assert.True(taggedAtCreation);
        Assert.Equal(1, owner.Attached);
        Assert.Contains(boxed.Globals, entry => entry.Interface == "wl_compositor");
        var window = Assert.Single(harness.Shell.Windows);
        Assert.Equal("foot · box", window.DecoratedTitle);

        owner.CloseClients();
        harness.PumpUntil(() => harness.Shell.Windows.Count == 0, "closing the owner's clients left the window");
        adoption.Dispose();
        adoption.Dispose();
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
