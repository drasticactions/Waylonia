using Waylonia.Sessions;

namespace Waylonia.Ui;

internal static class SessionMenu
{
    public const string ManagerLabel = "Sessions…";

    public const string SettingsLabel = "Settings…";

    public const string RefreshLabel = "Refresh applications";

    public static string Glyph(SessionStatus status) => status switch
    {
        SessionStatus.Connected => "●",
        SessionStatus.Connecting => "…",
        _ => "○",
    };

    public static string Label(string name, SessionStatus status) => $"{Glyph(status)} {name}";

    public static IReadOnlyList<ApplicationMenuItem> Build(
        IReadOnlyList<SessionMenuEntry> sessions,
        IReadOnlyList<ApplicationMenuItem>? localApplications,
        string? localNotice,
        Action? localRefresh,
        Action? openManager,
        Action? openSettings = null)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        var items = new List<ApplicationMenuItem>();
        if (openManager is not null)
        {
            items.Add(new ApplicationMenuItem(ManagerLabel, Invoke: openManager));
        }

        if (openSettings is not null)
        {
            items.Add(new ApplicationMenuItem(SettingsLabel, Invoke: openSettings));
        }

        if (openManager is not null || openSettings is not null)
        {
            items.Add(Separator());
        }

        if (localApplications is not null || localNotice is not null)
        {
            if (localApplications is { Count: > 0 })
            {
                items.AddRange(localApplications);
            }
            else
            {
                items.Add(new ApplicationMenuItem(localNotice ?? "No applications found"));
            }

            if (localRefresh is not null)
            {
                items.Add(Separator());
                items.Add(new ApplicationMenuItem(RefreshLabel, Invoke: localRefresh));
            }

            items.Add(Separator());
        }

        foreach (var session in sessions)
        {
            items.Add(new ApplicationMenuItem(Label(session.Name, session.Status), Children: Submenu(session)));
        }

        if (sessions.Count > 0)
        {
            items.Add(Separator());
        }

        return items;
    }

    private static IReadOnlyList<ApplicationMenuItem> Submenu(SessionMenuEntry session)
    {
        var items = new List<ApplicationMenuItem>();
        if (session.Status == SessionStatus.Disconnected)
        {
            items.Add(new ApplicationMenuItem("Connect", Invoke: session.Connect));
            return items;
        }

        items.Add(new ApplicationMenuItem("Disconnect", Invoke: session.Disconnect));
        if (session.Applications is { Count: > 0 } applications)
        {
            items.Add(Separator());
            items.AddRange(applications.Select(item => item.InSession(session.Name)));
        }
        else if (session.Notice is { } notice)
        {
            items.Add(Separator());
            items.Add(new ApplicationMenuItem(notice));
        }

        if (session.Status == SessionStatus.Connected && (session.Applications is not null || session.Notice is not null))
        {
            items.Add(Separator());
            items.Add(new ApplicationMenuItem(RefreshLabel, Invoke: session.Refresh));
        }

        return items;
    }

    private static ApplicationMenuItem Separator() => new(string.Empty, Separator: true);
}
