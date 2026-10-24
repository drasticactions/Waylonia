using Basin.Freedesktop;

namespace Waylonia.Agent;

internal static class AgentLauncher
{
    public static AgentLaunchPlan? Plan(
        string? command, IReadOnlyList<string> args, string? desktopId, Func<string, DesktopEntry?> findDesktop, out string? error)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(findDesktop);
        if (command is null == desktopId is null)
        {
            error = "Give exactly one of 'command' and 'desktop_id'.";
            return null;
        }

        if (desktopId is { } id)
        {
            if (args.Count > 0)
            {
                error = "'args' goes only with 'command'. A .desktop entry carries its own command line.";
                return null;
            }

            var fullId = id.EndsWith(".desktop", StringComparison.Ordinal) ? id : id + ".desktop";
            if (fullId.Contains('/', StringComparison.Ordinal))
            {
                error = $"'{id}' is not a .desktop id. Give an id from waylonia/launchable, such as org.gnome.TextEditor.";
                return null;
            }

            if (findDesktop(fullId) is not { } desktop)
            {
                error = $"No .desktop entry named {fullId} is installed on this machine. List the installed ones with waylonia/launchable.";
                return null;
            }

            if (desktop.LaunchArgv(null) is not { Length: > 0 } argv)
            {
                error = $"{fullId} runs in a terminal or has no command, and the agent has no terminal to wrap it in. Start a terminal such as foot with the program in 'args'.";
                return null;
            }

            error = null;
            return new AgentLaunchPlan(argv, Stem(fullId), fullId);
        }

        var program = command!.Trim();
        if (program.Length == 0)
        {
            error = "'command' is empty. Give the program to start, such as foot.";
            return null;
        }

        if (program.Any(char.IsWhiteSpace) && !File.Exists(program))
        {
            error = $"'{program}' has spaces, and there is no shell to split it. Put the program in 'command' and its arguments in 'args'.";
            return null;
        }

        error = null;
        return new AgentLaunchPlan([program, .. args], Path.GetFileName(program), null);
    }

    public static string Key(AgentLaunchPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return plan.DesktopId ?? Path.GetFileName(plan.Argv[0]);
    }

    private static string Stem(string desktopId) => desktopId[..^".desktop".Length];
}
