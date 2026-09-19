namespace Waylonia.Sessions;

internal static class ResumePolicy
{
    public static IReadOnlyList<string> Plan(IEnumerable<string> live, SessionCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(live);
        ArgumentNullException.ThrowIfNull(catalog);
        var planned = new List<string>();
        foreach (var name in live)
        {
            if (catalog.Find(name) is { Autoconnect: true } && !planned.Contains(name))
            {
                planned.Add(name);
            }
        }

        return planned;
    }
}
