namespace Waylonia;

internal sealed record HostProfile(
    string Ssh,
    string? Command,
    string? Compress,
    string? Terminal = null,
    string? CurrentDesktop = null);
