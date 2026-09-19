using Basin.Diagnostics;

namespace Waylonia;

internal static class HotkeyLines
{
    public static string Render(IReadOnlyList<Hotkey>? hotkeys) =>
        hotkeys is null ? string.Empty : string.Join('\n', hotkeys.Select(static hotkey => $"{hotkey.Chord} = {hotkey.Command}"));

    public static List<Hotkey>? Parse(string? text, string? session, out string? problem)
    {
        var hotkeys = new List<Hotkey>();
        foreach (var line in Lines(text))
        {
            var split = line.IndexOf('=', StringComparison.Ordinal);
            if (split <= 0)
            {
                problem = $"Write one hotkey per line as CHORD = COMMAND. '{line}' has no '='.";
                return null;
            }

            var chord = line[..split].Trim().Trim('"');
            var command = line[(split + 1)..].Trim().Trim('"');
            if (command.Length == 0)
            {
                problem = $"'{chord}' needs a command after the '='.";
                return null;
            }

            if (Hotkey.Parse(chord, command, BasinLogger.None, session) is not { } hotkey)
            {
                problem = $"'{chord}' is not a chord. Use modifiers and one key, such as ctrl+alt+t.";
                return null;
            }

            if (hotkeys.Any(other => other.SameChord(hotkey)))
            {
                problem = $"'{chord}' is listed twice. Keep one line per chord.";
                return null;
            }

            hotkeys.Add(hotkey);
        }

        problem = null;
        return hotkeys;
    }

    public static List<string> Lines(string? text) =>
        (text ?? string.Empty).Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
}
