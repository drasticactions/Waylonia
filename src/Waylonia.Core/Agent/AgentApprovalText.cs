using System.Text.Json;

namespace Waylonia.Agent;

internal static class AgentApprovalText
{
    public const int TextPreview = 80;

    public static AgentApprovalPrompt For(
        string method, string arguments, TimeSpan timeout, string? window = null, string? commandLine = null,
        string? commandKey = null, string? launch = null)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(arguments);
        return method switch
        {
            "waylonia/launch" => new(
                "Start a program",
                commandLine,
                "The agent wants to start a program that is not on this profile's allowlist.",
                $"Yes, and don't ask again for {commandKey ?? "this program"} this run",
                method, arguments, timeout),
            "windows/close" => new(
                "Close window",
                window,
                "The agent wants to close this window.",
                "Yes, and don't ask again for closing windows this run",
                method, arguments, timeout),
            "process/kill" => new(
                "End a program",
                launch,
                "The agent wants to end this program and everything it started.",
                "Yes, and don't ask again for ending programs this run",
                method, arguments, timeout),
            "clipboard/read" => new(
                "Read the clipboard",
                Kind(arguments) == "primary" ? "The primary selection" : "The clipboard",
                "The agent wants to read what is on its session's clipboard.",
                "Yes, and don't ask again for reading the clipboard this run",
                method, arguments, timeout),
            "clipboard/write" => new(
                "Write to the clipboard",
                Text(arguments) is { } text ? Quote(text) : null,
                "The agent wants to put this text on its session's clipboard.",
                "Yes, and don't ask again for writing the clipboard this run",
                method, arguments, timeout),
            _ => new(
                method,
                null,
                "The agent wants to make a call that this profile holds for approval.",
                $"Yes, and don't ask again for {method} this run",
                method, arguments, timeout),
        };
    }

    public static string Countdown(TimeSpan remaining)
    {
        var seconds = Math.Max(0, (int)Math.Ceiling(remaining.TotalSeconds));
        return $"No answer in {seconds / 60}:{seconds % 60:D2} means No.";
    }

    public static string Details(AgentApprovalPrompt prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        return $"{prompt.Method}\n{prompt.Arguments}";
    }

    public static string Window(string title, string appId)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(appId);
        return appId.Length == 0 || appId == title ? title : $"{title}  ({appId})";
    }

    private static string Quote(string text)
    {
        var line = text.ReplaceLineEndings(" ");
        return line.Length <= TextPreview ? $"\"{line}\"" : $"\"{line[..TextPreview]}…\" ({text.Length} characters)";
    }

    private static string? Kind(string arguments) => Property(arguments, "kind");

    private static string? Text(string arguments) => Property(arguments, "text");

    private static string? Property(string arguments, string name)
    {
        try
        {
            using var document = JsonDocument.Parse(arguments);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty(name, out var value)
                && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
