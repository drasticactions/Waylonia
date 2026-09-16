namespace Waylonia.Sessions;

internal sealed class OutputTail(int capacity = OutputTail.DefaultCapacity)
{
    public const int DefaultCapacity = 40;

    private readonly Queue<string> _lines = new(capacity);

    public void Add(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        lock (_lines)
        {
            if (_lines.Count == capacity)
            {
                _lines.Dequeue();
            }

            _lines.Enqueue(line);
        }
    }

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

    public void Clear()
    {
        lock (_lines)
        {
            _lines.Clear();
        }
    }
}
