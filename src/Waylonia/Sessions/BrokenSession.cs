namespace Waylonia.Sessions;

internal sealed record BrokenSession(string Name, string Path, string Error);
