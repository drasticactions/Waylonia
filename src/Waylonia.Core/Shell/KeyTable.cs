using Basin.Diagnostics;

namespace Waylonia.Shell;

internal sealed class KeyTable
{
    private readonly Dictionary<(HotkeyModifiers Modifiers, uint Code), string> _names;

    private KeyTable(IReadOnlyList<ShellKeyBinding> bindings)
    {
        Bindings = bindings;
        _names = bindings.ToDictionary(static binding => (binding.Modifiers, binding.Code), static binding => binding.Name);
    }

    public static KeyTable Empty { get; } = new([]);

    public IReadOnlyList<ShellKeyBinding> Bindings { get; }

    public string? Match(HotkeyModifiers held, uint code) =>
        _names.TryGetValue((held, code), out var name) ? name : null;

    public static KeyTable Build(IReadOnlyList<ShellKey> configured, IReadOnlyList<Hotkey> hostHotkeys, BasinLogger log)
    {
        ArgumentNullException.ThrowIfNull(configured);
        ArgumentNullException.ThrowIfNull(hostHotkeys);
        var chords = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var name in ShellKeyNames.All)
        {
            chords[name] = ShellKeyNames.DefaultChord(name);
        }

        foreach (var key in configured)
        {
            if (ShellKeyNames.IsKnown(key.Name))
            {
                var chord = key.Chord.Trim();
                chords[key.Name] = chord.Length == 0 ? null : chord;
            }
        }

        var host = new HashSet<(HotkeyModifiers, uint)>();
        foreach (var hotkey in hostHotkeys)
        {
            var code = CaptureChord.CodeFor(hotkey.Key);
            if (code != 0)
            {
                host.Add((hotkey.Modifiers, code));
            }
        }

        var owners = new Dictionary<(HotkeyModifiers, uint), string>();
        var bindings = new List<ShellKeyBinding>();
        foreach (var name in ShellKeyNames.All)
        {
            if (chords[name] is not { } chord)
            {
                continue;
            }

            if (ChordProblem(chord, out var modifiers, out var code) is { } problem)
            {
                log.Warn($"shell key {name}: {problem}");
                continue;
            }

            if (host.Contains((modifiers, code)))
            {
                log.Warn($"shell key {name} uses {chord}, which [hotkeys] already binds host-globally, keeping the hotkey");
                continue;
            }

            if (owners.TryGetValue((modifiers, code), out var owner))
            {
                log.Warn($"shell key {owner} and {name} both use {chord}, keeping {owner}");
                continue;
            }

            owners[(modifiers, code)] = name;
            bindings.Add(new ShellKeyBinding(name, chord, modifiers, code));
        }

        return new KeyTable(bindings);
    }

    public static string? ChordProblem(string chord, out HotkeyModifiers modifiers, out uint code)
    {
        ArgumentNullException.ThrowIfNull(chord);
        modifiers = HotkeyModifiers.None;
        code = 0;
        var tokens = chord.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            return $"'{chord}' names no key";
        }

        for (var i = 0; i < tokens.Length - 1; i++)
        {
            if (Hotkey.ModifierNamed(tokens[i]) is { } modifier)
            {
                modifiers |= modifier;
            }
            else
            {
                return $"'{chord}' has no modifier named '{tokens[i]}'; the modifiers are shift, ctrl, alt and super";
            }
        }

        code = CaptureChord.CodeFor(tokens[^1]);
        return code == 0 ? $"'{chord}' names no key" : null;
    }
}
