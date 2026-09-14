using Basin.Freedesktop;

namespace Waylonia;

internal static class ApplicationMenu
{
    public static IReadOnlyList<ApplicationMenuItem> Build(
        IEnumerable<DesktopEntry> entries, IReadOnlyList<string>? terminal)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var items = new List<ApplicationMenuItem>();
        foreach (var group in DesktopCategories.Group(entries))
        {
            var launchers = Launchers(group.Entries, terminal);
            if (launchers.Count > 0)
            {
                items.Add(new ApplicationMenuItem(DesktopCategories.DefaultLabel(group.Category), Children: launchers));
            }
        }

        return items;
    }

    public static string? CommandFor(DesktopEntry entry, IReadOnlyList<string>? terminal)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.LaunchArgv(terminal) is { } argv ? InWorkingDirectory(entry.WorkingDirectory, argv) : null;
    }

    public static IReadOnlyList<string>? Terminal(string? text) =>
        text is null || ExecLine.Split(text) is not { Length: > 0 } argv ? null : argv;

    public static IReadOnlySet<string>? CurrentDesktop(string? text)
    {
        if (text is null)
        {
            return null;
        }

        var names = text.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return names.Length == 0 ? null : new HashSet<string>(names, StringComparer.Ordinal);
    }

    private static List<ApplicationMenuItem> Launchers(IReadOnlyList<DesktopEntry> entries, IReadOnlyList<string>? terminal)
    {
        var sorted = new List<DesktopEntry>(entries);
        sorted.Sort(static (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        var items = new List<ApplicationMenuItem>(sorted.Count);
        foreach (var entry in sorted)
        {
            if (CommandFor(entry, terminal) is not { } command)
            {
                continue;
            }

            var actions = Actions(entry, terminal);
            items.Add(actions.Count == 0
                ? new ApplicationMenuItem(entry.Name, command)
                : new ApplicationMenuItem(entry.Name, Children:
                [
                    new ApplicationMenuItem(entry.Name, command),
                    new ApplicationMenuItem(string.Empty, Separator: true),
                    .. actions,
                ]));
        }

        return items;
    }

    private static List<ApplicationMenuItem> Actions(DesktopEntry entry, IReadOnlyList<string>? terminal)
    {
        var items = new List<ApplicationMenuItem>();
        foreach (var action in entry.Actions)
        {
            var argv = action.Argv;
            if (argv.Length == 0)
            {
                continue;
            }

            if (entry.Terminal)
            {
                if (terminal is not { Count: > 0 })
                {
                    continue;
                }

                argv = [.. terminal, .. argv];
            }

            items.Add(new ApplicationMenuItem(action.Name, InWorkingDirectory(entry.WorkingDirectory, argv)));
        }

        return items;
    }

    private static string InWorkingDirectory(string? directory, string[] argv) =>
        directory is { Length: > 0 }
            ? $"{ExecLine.Join(["cd", directory])} && exec {ExecLine.Join(argv)}"
            : ExecLine.Join(argv);
}
