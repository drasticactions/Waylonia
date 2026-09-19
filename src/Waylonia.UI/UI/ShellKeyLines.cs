using Basin.Shell.Nested;
using Waylonia.Shell;

namespace Waylonia.UI;

internal static class ShellKeyLines
{
    public static string Render(IReadOnlyList<ShellKey>? keys) =>
        keys is null ? string.Empty : string.Join('\n', keys.Select(static key => $"{key.Name} = {key.Chord}"));

    public static List<ShellKey>? Parse(string? text, out string? problem)
    {
        var keys = new List<ShellKey>();
        foreach (var line in HotkeyLines.Lines(text))
        {
            var split = line.IndexOf('=', StringComparison.Ordinal);
            if (split <= 0)
            {
                problem = $"Write one key per line as NAME = CHORD. '{line}' has no '='.";
                return null;
            }

            var name = line[..split].Trim().Trim('"');
            var chord = line[(split + 1)..].Trim().Trim('"');
            if (!ShellKeyNames.IsKnown(name))
            {
                problem = $"'{name}' is not a key. The keys are {string.Join(", ", ShellKeyNames.All.Take(4))} and so on.";
                return null;
            }

            if (chord.Length > 0 && KeyTable.ChordProblem(chord, out _, out _) is { } chordProblem)
            {
                problem = $"{name}: {chordProblem}.";
                return null;
            }

            if (keys.Any(other => other.Name == name))
            {
                problem = $"'{name}' is listed twice. Keep one line per key.";
                return null;
            }

            keys.Add(new ShellKey(name, chord));
        }

        problem = null;
        return keys;
    }
}
