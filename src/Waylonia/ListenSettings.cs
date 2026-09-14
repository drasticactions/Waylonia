using Basin.Capabilities;
using Basin.Transport.Waypipe;

namespace Waylonia;

internal sealed record ListenSettings(
    string Endpoint,
    WaypipeCompression Compression,
    bool Gpu,
    string? Video,
    IVideoDecoder? VideoDecoder);
