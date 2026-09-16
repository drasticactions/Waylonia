using Basin.Diagnostics;

namespace Waylonia;

internal sealed record Hotkey(string Chord, HotkeyModifiers Modifiers, string Key, string Command, string? Session = null)
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

        var modifiers = HotkeyModifiers.None;
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

    public static HotkeyModifiers? ModifierNamed(string token) => token.Trim().ToLowerInvariant() switch
    {
        "shift" => HotkeyModifiers.Shift,
        "ctrl" or "control" => HotkeyModifiers.Ctrl,
        "alt" or "option" => HotkeyModifiers.Alt,
        "super" or "cmd" or "command" or "win" or "logo" => HotkeyModifiers.Super,
        _ => null,
    };

    public bool SameChord(Hotkey other) => Modifiers == other.Modifiers && Key == other.Key;
}
