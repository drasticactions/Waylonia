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
            error = $"--agent {name} is not a profile name; use letters, digits, '.', '_' and '-'";
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
                log.Info($"wrote a placeholder {file}; its allowlist is empty");
            }
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            error = $"the agent profile {directory} cannot be created: {failure.Message}";
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
                log.Warn($"agent.toml size takes WxH, such as 1280x800, ignoring '{sizeText}'");
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
                log.Warn($"agent.toml scale takes 0.5 to 4, ignoring '{scale}'");
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
                log.Warn($"agent.toml takeover-timeout takes 0 to 3600 seconds, ignoring '{timeout}'");
            }
        }

        if (Config.Text(table, "placement") is { } placement)
        {
            profile = placement switch
            {
                "maximize" => profile with { Placement = PlacementMode.Maximize },
                "automatic" => profile with { Placement = PlacementMode.Automatic },
                _ => Warned(profile, log, $"agent.toml placement takes maximize or automatic, ignoring '{placement}'"),
            };
        }

        if (table.TryGetValue("launch", out var launchValue) && launchValue is TomlTable launch)
        {
            profile = profile with { Ask = TomlConfig.Flag(launch, "ask", profile.Ask), Allow = Allowlist(launch, log) };
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
                        log.Warn($"agent.toml [approve] actions takes {string.Join(", ", AgentApprovals.Names)}, ignoring '{item}'");
                    }
                }
            }
            else
            {
                log.Warn($"agent.toml [approve] actions is a list of names");
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
                    _ => Warned(profile, log, $"agent.toml [audit] screenshots takes none, acting or all, ignoring '{screenshots}'"),
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
            error = $"another waylonia --agent {profile.Name} is running; one run holds a profile at a time";
            return null;
        }
        catch (UnauthorizedAccessException failure)
        {
            error = $"the agent profile lock {profile.LockFile} cannot be opened: {failure.Message}";
            return null;
        }
    }

    private static AgentAllowlist Allowlist(TomlTable launch, BasinLogger log)
    {
        if (!launch.TryGetValue("allow", out var value))
        {
            return AgentAllowlist.Empty;
        }

        if (value is not TomlArray array)
        {
            log.Warn($"agent.toml [launch] allow is a list of commands and .desktop ids");
            return AgentAllowlist.Empty;
        }

        var entries = new List<AgentAllowEntry>();
        foreach (var item in array)
        {
            var entry = item switch
            {
                string text => AgentAllowlist.Parse(text),
                TomlArray argv when argv.All(static part => part is string) => AgentAllowlist.Parse(argv.Cast<string>().ToArray()),
                _ => null,
            };
            if (entry is null)
            {
                log.Warn($"agent.toml [launch] allow has an entry that is neither a command nor an argv list, ignoring '{item}'");
                continue;
            }

            entries.Add(entry);
        }

        return new AgentAllowlist(entries);
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

        [launch]
        # What waylonia/launch may start: a command, matched by the basename of
        # its first word, or a .desktop id. Words after the command are always
        # passed first, for example "chromium --force-renderer-accessibility".
        allow = []
        # Ask a person about anything else instead of refusing it.
        ask = true

        [approve]
        # What waits for a person: launch-unlisted, clipboard-read,
        # clipboard-write, close-window, kill.
        #actions = ["launch-unlisted", "clipboard-read", "close-window"]

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
