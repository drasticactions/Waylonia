namespace Waylonia.Agent;

internal static class AgentApprovals
{
    public const string Launch = "launch";

    public const string ClipboardRead = "clipboard-read";

    public const string ClipboardWrite = "clipboard-write";

    public const string CloseWindow = "close-window";

    public const string Kill = "kill";

    public static IReadOnlyList<string> Names { get; } = [Launch, ClipboardRead, ClipboardWrite, CloseWindow, Kill];

    public static string? MethodOf(string action) => action switch
    {
        Launch => "waylonia/launch",
        ClipboardRead => "clipboard/read",
        ClipboardWrite => "clipboard/write",
        CloseWindow => "windows/close",
        Kill => "process/kill",
        _ => null,
    };

    public static string? ActionOf(string method) => method switch
    {
        "waylonia/launch" => Launch,
        "clipboard/read" => ClipboardRead,
        "clipboard/write" => ClipboardWrite,
        "windows/close" => CloseWindow,
        "process/kill" => Kill,
        _ => null,
    };

    public static string Reason(string action) => action switch
    {
        Launch => "The agent wants to start a program.",
        ClipboardRead => "The agent wants to read the clipboard of its own session.",
        ClipboardWrite => "The agent wants to put text on the clipboard of its own session.",
        CloseWindow => "The agent wants to close a window.",
        Kill => "The agent wants to end a program it started.",
        _ => $"The agent wants to call {action}.",
    };

    public static string LaunchReason(IReadOnlyList<string> argv)
    {
        ArgumentNullException.ThrowIfNull(argv);
        return $"The agent wants to start this program: {string.Join(' ', argv)}";
    }
}
