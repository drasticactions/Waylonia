using System.Runtime.InteropServices;

namespace Waylonia.Sessions;

[System.Runtime.Versioning.SupportedOSPlatform("linux")]
internal static class HostSignals
{
    public static int? Number(string name) => name switch
    {
        "HUP" => 1,
        "INT" => 2,
        "QUIT" => 3,
        "KILL" => 9,
        "USR1" => 10,
        "USR2" => 12,
        "TERM" => 15,
        "CONT" => 18,
        "STOP" => 19,
        _ => null,
    };

    public static bool SignalGroup(int pid, string name) =>
        pid > 1 && Number(name) is { } number && kill(-pid, number) == 0;

    public static bool IsAlive(int pid) => pid > 0 && kill(pid, 0) == 0;

    [DllImport("libc", SetLastError = true)]
    private static extern int kill(int pid, int signal);
}
