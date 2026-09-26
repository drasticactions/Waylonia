using Basin.Freedesktop;

namespace Waylonia.Agent;

internal static class AgentLauncher
{
    public static AgentLaunchPlan? Plan(
        AgentAllowlist allowlist, string? command, IReadOnlyList<string> args, string? desktopId, Func<string, DesktopEntry?> findDesktop, out string? error)
    {
        ArgumentNullException.ThrowIfNull(allowlist);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(findDesktop);
        if (command is null == desktopId is null)
        {
            error = "name 'command' or 'desktop_id', not both and not neither";
            return null;
        }

        if (desktopId is { } id)
        {
            if (args.Count > 0)
            {
                error = "'args' goes with 'command'; a .desktop entry carries its own command line";
                return null;
            }

            var fullId = id.EndsWith(".desktop", StringComparison.Ordinal) ? id : id + ".desktop";
            if (fullId.Contains('/', StringComparison.Ordinal))
            {
                error = $"'{id}' is not a .desktop id";
                return null;
            }

            var entry = allowlist.MatchDesktop(fullId);
            if (findDesktop(fullId) is not { } desktop)
            {
                error = $"no .desktop entry is named {fullId} on this machine";
                return null;
            }

            if (desktop.LaunchArgv(null) is not { Length: > 0 } argv)
            {
                error = $"{fullId} runs in a terminal or has no command, and the agent has no terminal to wrap it in";
                return null;
            }

            error = null;
            return new AgentLaunchPlan(argv, Stem(fullId), entry is not null, entry, fullId);
        }

        var program = command!.Trim();
        if (program.Length == 0)
        {
            error = "'command' is empty";
            return null;
        }

        if (program.Any(char.IsWhiteSpace) && !File.Exists(program))
        {
            error = $"'{program}' has spaces; name the program in 'command' and its arguments in 'args', since there is no shell";
            return null;
        }

        var listed = allowlist.MatchCommand(program);
        var full = listed is null
            ? (IReadOnlyList<string>)[program, .. args]
            : [listed.Program, .. listed.Arguments, .. args];
        error = null;
        return new AgentLaunchPlan(full, Path.GetFileName(full[0]), listed is not null, listed, null);
    }

    public static string Key(AgentLaunchPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return plan.DesktopId ?? Path.GetFileName(plan.Argv[0]);
    }

    private static string Stem(string desktopId) => desktopId[..^".desktop".Length];
}
