using Basin.Diagnostics;
using Basin.Shell.Nested;

namespace Waylonia;

internal sealed record CaptureChord(string Text, bool DoubleTap, uint Code, ShellModifiers Modifiers)
{
    public const int DoubleTapMillis = 400;

    public static uint CodeFor(string name) => ShellKeyCodes.CodeFor(name);

    public static CaptureChord? Parse(string text, BasinLogger log)
    {
        if (text is null || text.Trim().Length == 0)
        {
            return null;
        }

        var trimmed = text.Trim();
        var doubleTap = trimmed.StartsWith("double:", StringComparison.OrdinalIgnoreCase);
        var body = doubleTap ? trimmed["double:".Length..] : trimmed;
        var tokens = body.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            log.Warn($"capture: capture-chord '{trimmed}' names no key, capture is off");
            return null;
        }

        var modifiers = ShellModifiers.None;
        for (var i = 0; i < tokens.Length - 1; i++)
        {
            switch (tokens[i].ToLowerInvariant())
            {
                case "shift":
                    modifiers |= ShellModifiers.Shift;
                    break;
                case "ctrl" or "control":
                    modifiers |= ShellModifiers.Ctrl;
                    break;
                case "alt" or "option":
                    modifiers |= ShellModifiers.Alt;
                    break;
                case "super" or "cmd" or "command" or "win" or "logo":
                    modifiers |= ShellModifiers.Super;
                    break;
                default:
                    log.Warn($"capture: unknown modifier '{tokens[i]}' in capture-chord '{trimmed}', capture is off");
                    return null;
            }
        }

        var code = CodeFor(tokens[^1]);
        if (code == 0)
        {
            log.Warn($"capture: unknown key '{tokens[^1]}' in capture-chord '{trimmed}', capture is off");
            return null;
        }

        if (doubleTap && modifiers != ShellModifiers.None)
        {
            log.Warn($"capture: a double-tap capture-chord names one key alone, modifiers in '{trimmed}' ignored");
            modifiers = ShellModifiers.None;
        }

        return new CaptureChord(trimmed, doubleTap, code, modifiers);
    }

    public static ShellModifiers ModifierOf(uint code) => ShellKeyCodes.ModifierOf(code);
}
