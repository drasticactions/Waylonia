using static Waylonia.WayloniaLog;

namespace Waylonia;

internal sealed class Relay(string name)
{
    private const int Kept = 20;
    private readonly Queue<string> _lines = new(Kept);

    public string Name => name;

    public void Watch(StreamReader reader, Func<string, bool>? claim = null) => _ = Task.Run(async () =>
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            if (claim is not null && claim(line))
            {
                continue;
            }

            lock (_lines)
            {
                if (_lines.Count == Kept)
                {
                    _lines.Dequeue();
                }

                _lines.Enqueue(line);
            }
        }
    });

    public IReadOnlyList<string> Lines()
    {
        lock (_lines)
        {
            return [.. _lines];
        }
    }

    public string? LastLine()
    {
        lock (_lines)
        {
            return _lines.Count > 0 ? _lines.Last() : null;
        }
    }

    public void Report()
    {
        foreach (var line in Lines())
        {
            Log.Error($"{name}: {line}");
        }
    }
}
