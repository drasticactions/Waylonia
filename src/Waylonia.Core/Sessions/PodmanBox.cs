namespace Waylonia.Sessions;

internal sealed record PodmanBox(
    DestinationKind Kind,
    string Program,
    IReadOnlyList<string> RunPrefix,
    IReadOnlyList<string> ListArguments,
    bool ClearsEnvironment,
    bool PipeTable)
{
    public static PodmanBox Distrobox { get; } = new(
        DestinationKinds.Distrobox, "distrobox", ["enter", "--no-workdir", "{0}", "--"], ["list", "--no-color"], ClearsEnvironment: true, PipeTable: true);

    public static PodmanBox Toolbox { get; } = new(
        DestinationKinds.Toolbox, "toolbox", ["run", "-c", "{0}"], ["list", "--containers"], ClearsEnvironment: false, PipeTable: false);

    public static IReadOnlyList<string> KeptVariables { get; } = ["HOME", "USER", "PATH", "XDG_RUNTIME_DIR", "LANG"];

    public static PodmanBox? For(DestinationKind kind) =>
        kind == DestinationKinds.Distrobox ? Distrobox : kind == DestinationKinds.Toolbox ? Toolbox : null;

    public IReadOnlyList<string> Run(string name, string script)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(script);
        return [.. RunPrefix.Select(part => part == "{0}" ? name : part), "sh", "-c", PidMarker.Wrapper, "sh", script];
    }

    public IReadOnlyDictionary<string, string>? Environment(IReadOnlyDictionary<string, string> host)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (!ClearsEnvironment)
        {
            return null;
        }

        return host
            .Where(static pair => KeptVariables.Contains(pair.Key) || pair.Key.StartsWith("CONTAINER_", StringComparison.Ordinal))
            .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
    }

    public IReadOnlyList<string> ParseList(string output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var names = new List<string>();
        var lines = output.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines.Skip(1))
        {
            var name = PipeTable
                ? line.Split('|', StringSplitOptions.TrimEntries) is [_, var column, ..] ? column : null
                : line.Split(' ', StringSplitOptions.RemoveEmptyEntries) is [_, var second, ..] ? second : null;
            if (name is not null && Kind.NameProblem(name) is null)
            {
                names.Add(name);
            }
        }

        return names;
    }
}
