using Basin.Diagnostics;
using Basin.Shell.Nested;
using Tomlyn.Model;
using Waylonia.Cli;

namespace Waylonia.Agent;

internal static class AgentProfileStore
{
    private const UnixFileMode Private = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    public static string RootFor(WayloniaPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var state = paths.StateFile.Length > 0 && Path.GetDirectoryName(paths.StateFile) is { Length: > 0 } directory
            ? directory
            : Path.Combine(
                WayloniaPaths.XdgHome(
                    "XDG_STATE_HOME",
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "state")),
                "waylonia");
        return Path.Combine(state, "agents");
    }

    public static bool IsValidName(string name) =>
        name.Length > 0 && name.Length <= 64 && name[0] != '.'
        && name.All(static c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');

    public static AgentProfile? Load(string root, string name, BasinLogger log, out string? error)
    {
        ArgumentException.ThrowIfNullOrEmpty(root);
        ArgumentNullException.ThrowIfNull(name);
        if (!IsValidName(name))
        {
            error = $"profile: '{name}' is not a profile name, use letters, digits, '.', '_' and '-'";
            return null;
        }

        var directory = Path.Combine(root, name);
        try
        {
            foreach (var path in (ReadOnlySpan<string>)[
                directory,
                Path.Combine(directory, "home"),
                Path.Combine(directory, "home", ".config"),
                Path.Combine(directory, "home", ".local", "share"),
                Path.Combine(directory, "home", ".local", "state"),
                Path.Combine(directory, "home", ".cache"),
                Path.Combine(directory, "logs"),
                Path.Combine(directory, "audit")])
            {
                if (!System.IO.Directory.Exists(path))
                {
                    if (OperatingSystem.IsWindows())
                    {
                        System.IO.Directory.CreateDirectory(path);
                    }
                    else
                    {
                        System.IO.Directory.CreateDirectory(path, Private);
                    }
                }
            }

            var file = Path.Combine(directory, AgentProfile.FileName);
            if (!File.Exists(file))
            {
                File.WriteAllText(file, Placeholder);
                log.Info($"profile: placeholder written to {file}");
            }
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            error = $"profile: {directory} cannot be created, {CliMessage.Text(failure.Message)}";
            return null;
        }

        error = null;
        var table = TomlConfig.Read(Path.Combine(directory, AgentProfile.FileName), log);
        return Parse(name, directory, table ?? [], log);
    }

    public static AgentProfile Parse(string name, string directory, TomlTable table, BasinLogger log)
    {
        ArgumentNullException.ThrowIfNull(table);
        var profile = new AgentProfile(name, directory);
        if (Config.Text(table, "size") is { } sizeText)
        {
            if (RunRules.ParseSize(sizeText) is { } size && size.Width <= 8192 && size.Height <= 8192)
            {
                profile = profile with { Width = size.Width, Height = size.Height };
            }
            else
            {
                log.Warn($"agent.toml: size takes WxH such as 1280x800, '{sizeText}' ignored");
            }
        }

        if (table.TryGetValue("scale", out var scale))
        {
            if (Number(scale) is { } factor && factor is >= 0.5 and <= 4)
            {
                profile = profile with { Scale = factor };
            }
            else
            {
                log.Warn($"agent.toml: scale takes 0.5 to 4, '{scale}' ignored");
            }
        }

        profile = profile with { Headless = TomlConfig.Flag(table, "headless", profile.Headless) };
        profile = profile with { Keymap = Config.Text(table, "keymap") };
        if (table.TryGetValue("takeover-timeout", out var timeout))
        {
            if (timeout is long seconds && seconds is >= 0 and <= 3600)
            {
                profile = profile with { TakeoverTimeout = (int)seconds };
            }
            else
            {
                log.Warn($"agent.toml: takeover-timeout takes 0 to 3600 seconds, '{timeout}' ignored");
            }
        }

        if (Config.Text(table, "placement") is { } placement)
        {
            profile = placement switch
            {
                "maximize" => profile with { Placement = PlacementMode.Maximize },
                "automatic" => profile with { Placement = PlacementMode.Automatic },
                _ => Warned(profile, log, $"agent.toml: placement takes maximize or automatic, '{placement}' ignored"),
            };
        }

        if (table.ContainsKey("launch"))
        {
            log.Warn($"agent.toml: [launch] is gone and ignored, add \"launch\" to [approve] actions to have a person approve each launch");
        }

        if (table.TryGetValue("approve", out var approveValue) && approveValue is TomlTable approve
            && approve.TryGetValue("actions", out var actionsValue))
        {
            var actions = new List<string>();
            if (actionsValue is TomlArray array)
            {
                foreach (var item in array)
                {
                    if (item is string action && AgentApprovals.Names.Contains(action))
                    {
                        actions.Add(action);
                    }
                    else
                    {
                        log.Warn($"agent.toml: [approve] actions takes {string.Join(", ", AgentApprovals.Names)}, '{item}' ignored");
                    }
                }
            }
            else
            {
                log.Warn($"agent.toml: [approve] actions is not a list of names, ignored");
            }

            profile = profile with { Approve = actions };
        }

        if (table.TryGetValue("audit", out var auditValue) && auditValue is TomlTable audit)
        {
            if (Config.Text(audit, "screenshots") is { } screenshots)
            {
                profile = screenshots switch
                {
                    "none" => profile with { Screenshots = AgentScreenshots.None },
                    "acting" => profile with { Screenshots = AgentScreenshots.Acting },
                    "all" => profile with { Screenshots = AgentScreenshots.All },
                    _ => Warned(profile, log, $"agent.toml: [audit] screenshots takes none, acting or all, '{screenshots}' ignored"),
                };
            }

            profile = profile with { AuditText = TomlConfig.Flag(audit, "text", profile.AuditText) };
        }

        if (table.TryGetValue("accessibility", out var accessibilityValue) && accessibilityValue is TomlTable accessibility)
        {
            profile = profile with { Accessibility = TomlConfig.Flag(accessibility, "enabled", profile.Accessibility) };
        }

        return profile;
    }

    public static IDisposable? Lock(AgentProfile profile, out string? error)
    {
        ArgumentNullException.ThrowIfNull(profile);
        try
        {
            var stream = new FileStream(profile.LockFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            stream.SetLength(0);
            var pid = System.Text.Encoding.ASCII.GetBytes(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            stream.Write(pid);
            stream.Flush();
            error = null;
            return stream;
        }
        catch (IOException)
        {
            error = $"profile {profile.Name}: another run holds the profile, stop the other waylonia --agent {profile.Name} first";
            return null;
        }
        catch (UnauthorizedAccessException failure)
        {
            error = $"profile {profile.Name}: lock {profile.LockFile} cannot be opened, {CliMessage.Text(failure.Message)}";
            return null;
        }
    }

    private static double? Number(object? value) => value switch
    {
        double number => number,
        long whole => whole,
        _ => null,
    };

    private static AgentProfile Warned(AgentProfile profile, BasinLogger log, string warning)
    {
        log.Warn($"{warning}");
        return profile;
    }

    public const string Placeholder = """
        # The policy of one Waylonia agent profile. `waylonia --agent NAME` reads
        # this file from $XDG_STATE_HOME/waylonia/agents/NAME/agent.toml, and
        # launched applications get home/ beside it as their HOME.
        #
        # What this does not do: the agent's applications run as your user, on
        # your filesystem and network. The profile keeps them out of your desktop,
        # your clipboard and your windows. It is not a sandbox. The control socket
        # is the boundary, and your uid is the socket's boundary: an agent that
        # can run commands as you can answer its own approvals.

        # The fixed output the applications see, and its scale.
        #size = "1280x800"
        #scale = 1.0

        # Run with no window. --headless does the same for one run.
        #headless = false

        # Every normal window maps maximized ("maximize"), or where the shell
        # would put it ("automatic").
        #placement = "maximize"

        # Seconds without host input after which a paused agent drives again.
        # 0 means only the Resume button ends a pause.
        #takeover-timeout = 10

        # The keyboard layout of a headless run.
        #keymap = "us"

        [approve]
        # What waits for a person: launch, clipboard-read, clipboard-write,
        # close-window, kill. waylonia/launch starts any program at once
        # unless launch is listed here.
        #actions = ["launch", "clipboard-read", "close-window"]

        [audit]
        # Screenshots in audit/: none, acting (before and after each call that
        # changes something) or all.
        #screenshots = "acting"
        # Log the text of input/text and clipboard/write instead of its hash.
        #text = false

        [accessibility]
        # Start the profile's accessibility bus and offer the waylonia/a11y-*
        # methods.
        #enabled = true

        """;
}
