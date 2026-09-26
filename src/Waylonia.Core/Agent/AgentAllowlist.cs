namespace Waylonia.Agent;

internal sealed class AgentAllowlist
{
    public AgentAllowlist(IReadOnlyList<AgentAllowEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        Entries = entries;
    }

    public static AgentAllowlist Empty { get; } = new([]);

    public IReadOnlyList<AgentAllowEntry> Entries { get; }

    public bool IsEmpty => Entries.Count == 0;

    public AgentAllowEntry? MatchCommand(string command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var name = Path.GetFileName(command);
        if (name.Length == 0 || name.EndsWith(".desktop", StringComparison.Ordinal))
        {
            return null;
        }

        foreach (var entry in Entries)
        {
            if (!entry.IsDesktop && Path.GetFileName(entry.Program) == name)
            {
                return entry;
            }
        }

        return null;
    }

    public AgentAllowEntry? MatchDesktop(string desktopId)
    {
        ArgumentNullException.ThrowIfNull(desktopId);
        var id = desktopId.EndsWith(".desktop", StringComparison.Ordinal) ? desktopId : desktopId + ".desktop";
        foreach (var entry in Entries)
        {
            if (entry.IsDesktop && entry.Program == id)
            {
                return entry;
            }
        }

        return null;
    }

    public static AgentAllowEntry? Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var words = Split(text);
        return words.Count == 0 ? null : new AgentAllowEntry(words[0], words.Skip(1).ToArray());
    }

    public static AgentAllowEntry? Parse(IReadOnlyList<string> argv)
    {
        ArgumentNullException.ThrowIfNull(argv);
        return argv.Count == 0 || argv[0].Trim().Length == 0 ? null : new AgentAllowEntry(argv[0].Trim(), argv.Skip(1).ToArray());
    }

    private static List<string> Split(string text)
    {
        var words = new List<string>();
        var current = new System.Text.StringBuilder();
        var quote = '\0';
        var any = false;
        foreach (var c in text)
        {
            if (quote != '\0')
            {
                if (c == quote)
                {
                    quote = '\0';
                }
                else
                {
                    current.Append(c);
                }
            }
            else if (c is '"' or '\'')
            {
                quote = c;
                any = true;
            }
            else if (char.IsWhiteSpace(c))
            {
                if (any || current.Length > 0)
                {
                    words.Add(current.ToString());
                    current.Clear();
                    any = false;
                }
            }
            else
            {
                current.Append(c);
            }
        }

        if (any || current.Length > 0)
        {
            words.Add(current.ToString());
        }

        return words;
    }
}
