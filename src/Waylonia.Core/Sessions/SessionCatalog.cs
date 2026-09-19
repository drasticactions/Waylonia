namespace Waylonia.Sessions;

internal sealed record SessionCatalog(IReadOnlyList<SessionProfile> Profiles, IReadOnlyList<BrokenSession> Broken)
{
    public static SessionCatalog Empty { get; } = new([], []);

    public SessionProfile? Find(string name) =>
        Profiles.FirstOrDefault(profile => string.Equals(profile.Name, name, StringComparison.Ordinal));
}
