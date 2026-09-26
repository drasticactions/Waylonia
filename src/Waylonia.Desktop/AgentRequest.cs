using Waylonia.Sessions;

namespace Waylonia;

internal sealed record AgentRequest(
    string Name,
    bool Headless,
    string? Size,
    double? Scale,
    HostCapabilities Capabilities,
    WayloniaPaths Paths,
    Config Config,
    SessionStore Store,
    string? SocketName,
    long Frames,
    string? Screenshot);
