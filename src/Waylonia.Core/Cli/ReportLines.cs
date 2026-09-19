using Basin.Diagnostics;

namespace Waylonia.Cli;

public static class ReportLines
{
    public static string Socket(string? name) =>
        string.IsNullOrEmpty(name) ? "SOCKET (inherited)" : $"SOCKET {name}";

    public static string Frames(long rendered) =>
        $"FRAMES {rendered} LIVE {(BasinCounters.Enabled ? BasinCounters.LiveObjects.ToString() : "untracked")}";
}
