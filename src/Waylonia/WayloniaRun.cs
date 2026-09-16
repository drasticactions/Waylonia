using Basin.Capabilities;
using Basin.Transport.Waypipe;
using Waylonia.Sessions;

namespace Waylonia;

internal sealed record WayloniaRun(
    HostSettings Host,
    IReadOnlyList<SessionSettings> Initial,
    bool Manager,
    string? LocalCommand,
    LocalDesktop? LocalDesktop,
    ListenSettings? WaypipeListen,
    long Frames,
    string? Screenshot,
    string? SocketName,
    Config Config,
    SessionStore Store,
    string AudioFormat = "f32",
    bool OpenSettings = false)
{
    public bool ChannelsWanted => Manager || Initial.Count > 0 || WaypipeListen is not null || !OperatingSystem.IsLinux();

    public bool ManagedTransport => ChannelsWanted && LocalCommand is null && LocalDesktop is null;

    public bool LocalOnly => !ManagedTransport;
}
