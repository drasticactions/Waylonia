using Basin.Transport.Waypipe;

namespace Waylonia.Sessions;

internal static class ListenHint
{
    private const string VsockPrefix = "vsock:";

    public static bool IsVsock(string endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        return endpoint.StartsWith(VsockPrefix, StringComparison.OrdinalIgnoreCase);
    }

    public static string CompressName(WaypipeCompression compression) => compression switch
    {
        WaypipeCompression.None => "none",
        WaypipeCompression.Zstd => "zstd",
        _ => "lz4",
    };

    public static string Arguments(WaypipeCompression compression, bool gpu, string? video) =>
        $"--compress {CompressName(compression)} {(gpu ? string.Empty : "--no-gpu ")}{(video is { } codec ? $"--video={codec} " : string.Empty)}";

    public static string? GuestCommand(string endpoint, bool wsl, WaypipeCompression compression, bool gpu, string? video)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        var arguments = Arguments(compression, gpu, video);
        if (IsVsock(endpoint))
        {
            var port = endpoint[VsockPrefix.Length..];
            var prefix = wsl ? "wsl -- " : string.Empty;
            return $"{prefix}waypipe --vsock -s {port} {arguments}server -- COMMAND";
        }

        return endpoint.Contains(':', StringComparison.Ordinal)
            ? null
            : $"waypipe --socket {endpoint} {arguments}server -- COMMAND";
    }
}
