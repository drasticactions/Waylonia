using Basin.Diagnostics;
using Basin.Shell.Nested;

namespace Waylonia;

internal sealed record Hotkey(string Chord, ShellModifiers Modifiers, string Key, string Command, string? Session = null)
{
    public static Hotkey? Parse(string chord, string? command, BasinLogger log, string? session = null)
    {
        if (command is null)
        {
            log.Warn($"hotkey '{chord}' has no command, skipping");
            return null;
        }

        var tokens = chord.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            log.Warn($"hotkey '{chord}' names no key, skipping");
            return null;
        }

        var modifiers = ShellModifiers.None;
        for (var i = 0; i < tokens.Length - 1; i++)
        {
            if (ModifierNamed(tokens[i]) is { } modifier)
            {
                modifiers |= modifier;
            }
            else
            {
                log.Warn($"unknown modifier '{(tokens[i])}' in hotkey '{chord}', skipping");
                return null;
            }
        }

        return new Hotkey(chord, modifiers, tokens[^1].ToLowerInvariant(), command, session);
    }

    public static ShellModifiers? ModifierNamed(string token) => ShellKeyCodes.ModifierNamed(token);

    public static IReadOnlyList<ShellChord> Reserved(IReadOnlyList<Hotkey> hotkeys)
    {
        ArgumentNullException.ThrowIfNull(hotkeys);
        var reserved = new List<ShellChord>(hotkeys.Count);
        foreach (var hotkey in hotkeys)
        {
            var code = CaptureChord.CodeFor(hotkey.Key);
            if (code != 0)
            {
                reserved.Add(new ShellChord(hotkey.Modifiers, code));
            }
        }

        return reserved;
    }

    public bool SameChord(Hotkey other) => Modifiers == other.Modifiers && Key == other.Key;
}
