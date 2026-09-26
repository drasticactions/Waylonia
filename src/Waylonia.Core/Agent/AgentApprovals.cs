namespace Waylonia.Agent;

internal static class AgentApprovals
{
    public const string LaunchUnlisted = "launch-unlisted";

    public const string ClipboardRead = "clipboard-read";

    public const string ClipboardWrite = "clipboard-write";

    public const string CloseWindow = "close-window";

    public const string Kill = "kill";

    public static IReadOnlyList<string> Names { get; } = [LaunchUnlisted, ClipboardRead, ClipboardWrite, CloseWindow, Kill];

    public static string? MethodOf(string action) => action switch
    {
        LaunchUnlisted => "waylonia/launch",
        ClipboardRead => "clipboard/read",
        ClipboardWrite => "clipboard/write",
        CloseWindow => "windows/close",
        Kill => "process/kill",
        _ => null,
    };

    public static string? ActionOf(string method) => method switch
    {
        "waylonia/launch" => LaunchUnlisted,
        "clipboard/read" => ClipboardRead,
        "clipboard/write" => ClipboardWrite,
        "windows/close" => CloseWindow,
        "process/kill" => Kill,
        _ => null,
    };

    public static string Reason(string action) => action switch
    {
        LaunchUnlisted => "the agent wants to start a program that the profile's allowlist does not name",
        ClipboardRead => "the agent wants to read the clipboard of its own session",
        ClipboardWrite => "the agent wants to put text on the clipboard of its own session",
        CloseWindow => "the agent wants to close a window",
        Kill => "the agent wants to end a program it started",
        _ => $"the agent wants to call {action}",
    };
}
