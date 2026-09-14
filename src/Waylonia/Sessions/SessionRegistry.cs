using Basin.Diagnostics;
using static Waylonia.WayloniaLog;

namespace Waylonia.Sessions;

internal sealed class SessionRegistry(ISessionHost host)
{
    private readonly List<SshSession> _sessions = [];

    public IReadOnlyList<SshSession> Sessions => _sessions;

    public IEnumerable<SshSession> Live => _sessions.Where(static session => session.IsLive);

    public bool AnyLive => _sessions.Any(static session => session.IsLive);

    public event Action? Changed;

    public event Action<SshSession, int>? SessionEnded;

    public SshSession? Get(string name) =>
        _sessions.FirstOrDefault(session => string.Equals(session.Name, name, StringComparison.Ordinal));

    public SshSession? AdHoc => _sessions.FirstOrDefault(static session => session.Settings.AdHoc);

    public SshSession Add(SessionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (Get(settings.Name) is { } existing)
        {
            if (!existing.IsLive && !ReferenceEquals(existing.Settings, settings))
            {
                existing.Replace(settings);
            }

            return existing;
        }

        var session = new SshSession(settings, host);
        session.Changed += _ => Changed?.Invoke();
        session.Ended += (ended, code) => SessionEnded?.Invoke(ended, code);
        _sessions.Add(session);
        Changed?.Invoke();
        return session;
    }

    public string? WhyRefused(SessionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var existing = Get(settings.Name);
        if (existing is { IsLive: true } && !ReferenceEquals(existing.Settings, settings))
        {
            return null;
        }

        return DesktopConflict(
            Live.Where(session => !ReferenceEquals(session, existing))
                .Select(static session => (session.Name, session.Settings.IsDesktop)),
            settings.IsDesktop);
    }

    public async Task<bool> ConnectAsync(SessionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (WhyRefused(settings) is { } why)
        {
            Log.Error($"{why}");
            host.Status(why);
            return false;
        }

        var session = Add(settings);
        return await session.ConnectAsync();
    }

    public async Task DisconnectAsync(string name)
    {
        if (Get(name) is { } session)
        {
            await session.DisconnectAsync();
        }
    }

    public async Task RemoveAsync(string name)
    {
        if (Get(name) is not { } session)
        {
            return;
        }

        await session.DisconnectAsync();
        session.Dispose();
        _sessions.Remove(session);
        Changed?.Invoke();
    }

    public IReadOnlyList<Hotkey> Hotkeys(IReadOnlyList<Hotkey> global, bool quiet = false) =>
        HotkeyUnion(global, Live.Select(static session => session.Settings.Hotkeys), quiet ? null : Log);

    public static IReadOnlyList<Hotkey> HotkeyUnion(
        IReadOnlyList<Hotkey> global, IEnumerable<IReadOnlyList<Hotkey>> sessions, BasinLogger? log)
    {
        ArgumentNullException.ThrowIfNull(global);
        ArgumentNullException.ThrowIfNull(sessions);
        var union = new List<Hotkey>();
        foreach (var hotkey in global.Concat(sessions.SelectMany(static list => list)))
        {
            if (union.FirstOrDefault(hotkey.SameChord) is { } taken)
            {
                log?.Warn(
                    $"hotkey '{hotkey.Chord}' of {Owner(hotkey)} is already bound by {Owner(taken)}, skipping it");
                continue;
            }

            union.Add(hotkey);
        }

        return union;
    }

    private static string Owner(Hotkey hotkey) => hotkey.Session is { } session ? $"session {session}" : "the [hotkeys] table";

    public static string? DesktopConflict(IEnumerable<(string Name, bool Desktop)> live, bool desktop)
    {
        ArgumentNullException.ThrowIfNull(live);
        if (!desktop)
        {
            return null;
        }

        foreach (var (name, isDesktop) in live)
        {
            if (isDesktop)
            {
                return $"{name} is already running a desktop; disconnect it before starting another";
            }
        }

        return null;
    }

    public static bool ExitsProcess(bool adHoc, bool hadClients, bool othersLive, bool manager) =>
        adHoc && !hadClients && !othersLive && !manager;

    public void DisposeAll()
    {
        foreach (var session in _sessions)
        {
            session.Dispose();
        }
    }
}
