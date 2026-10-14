namespace Waylonia.Sessions;

internal sealed record DestinationKind
{
    public required string Key { get; init; }

    public required string Label { get; init; }

    public required string Noun { get; init; }

    public required string Verb { get; init; }

    public required string Caption { get; init; }

    public required string HostOs { get; init; }

    public string? Placeholder { get; init; }

    public string? FormHint { get; init; }

    public string? UserNote { get; init; }

    public bool AllowsEmptyName { get; init; }

    public bool SharesHome { get; init; }

    public bool TakesUser { get; init; }

    public bool SlowFirstStart { get; init; }

    public string? DesktopProblem { get; init; }

    public required Func<string, string?> NameRule { get; init; }

    public string Prefix => IsSsh ? string.Empty : Key + ":";

    public bool IsSsh => Key == "ssh";

    public bool RunsDesktop => DesktopProblem is null;

    public string For(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return Prefix + name;
    }

    public bool TryParse(string destination, out string name)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!IsSsh && destination.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            name = destination[Prefix.Length..];
            return true;
        }

        name = string.Empty;
        return false;
    }

    public string? NameProblem(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name.Length == 0 && !AllowsEmptyName)
        {
            return $"{Noun} needs a name; there is no default one";
        }

        return name.Length == 0 ? null : NameRule(name);
    }

    public string Unreachable(string destination) =>
        $"{destination} names {Noun}, and only waylonia on {HostOs} reaches one";

    public bool Equals(DestinationKind? other) => other is not null && other.Key == Key;

    public override int GetHashCode() => Key.GetHashCode(StringComparison.Ordinal);

    public override string ToString() => Label;
}
