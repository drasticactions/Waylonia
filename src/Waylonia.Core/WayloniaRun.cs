using Basin.Capabilities;
using Basin.Transport.Waypipe;
using Waylonia.Agent;
using Waylonia.Sessions;

namespace Waylonia;

internal sealed record WayloniaRun(
    HostCapabilities Capabilities,
    WayloniaPaths Paths,
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
    bool OpenSettings = false,
    AgentProfile? Agent = null,
    bool Headless = false)
{
    public bool ChannelsWanted => Manager || Initial.Count > 0 || WaypipeListen is not null || Capabilities.ChannelsOnly;

    public bool ManagedTransport => Agent is null && ChannelsWanted && LocalCommand is null && LocalDesktop is null;

    public bool LocalOnly => !ManagedTransport;
}
