namespace Waylonia.Sessions;

internal sealed record IncusUser(string Name, int Uid, int Gid, string Home)
{
    public static IncusUser Root { get; } = new("root", 0, 0, "/root");
}
