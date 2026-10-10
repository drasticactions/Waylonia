using Basin.Transport.Waypipe;
using Waylonia.Sessions;
using Xunit;

namespace Waylonia.Tests;

public sealed class ListenHintTests
{
    [Fact]
    public void A_wsl_listener_names_the_command_wsl_runs()
    {
        Assert.Equal(
            "wsl -- waypipe --vsock -s 9800 --compress lz4 --no-gpu server -- COMMAND",
            ListenHint.GuestCommand("vsock:9800", wsl: true, WaypipeCompression.Lz4, gpu: false, video: null));
    }

    [Fact]
    public void A_guest_listener_carries_the_channel_settings_the_peer_must_match()
    {
        Assert.Equal(
            "waypipe --vsock -s 9800 --compress zstd --video=h264 server -- COMMAND",
            ListenHint.GuestCommand("VSOCK:9800", wsl: false, WaypipeCompression.Zstd, gpu: true, video: "h264"));
    }

    [Fact]
    public void A_unix_listener_names_its_socket_and_a_tcp_one_has_no_waypipe_command()
    {
        Assert.Equal(
            "waypipe --socket /tmp/wp.sock --compress none --no-gpu server -- COMMAND",
            ListenHint.GuestCommand("/tmp/wp.sock", wsl: false, WaypipeCompression.None, gpu: false, video: null));
        Assert.Null(ListenHint.GuestCommand("127.0.0.1:9800", wsl: false, WaypipeCompression.Lz4, gpu: false, video: null));
    }
}
