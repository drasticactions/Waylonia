using Basin.Diagnostics;
using Waylonia.Sessions;
using Waylonia.Shell;

namespace Waylonia;

internal static class RunRules
{
    internal static (SessionProfile? Profile, string? Error) DesktopProfileFor(
        string desktopName, string? ssh, SessionCatalog catalog, Config config, ref SessionOverrides overrides)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(overrides);
        var sshProfile = ssh is null ? null : catalog.Find(ssh);
        var sshDestination = ssh is null ? null : sshProfile?.Ssh ?? ssh;
        if (catalog.Find(desktopName) is { Desktop: not null } saved)
        {
            return (sshDestination is null ? saved : saved with { Ssh = sshDestination }, null);
        }

        var basis = sshProfile ?? new SessionProfile(desktopName, sshDestination ?? string.Empty);

        if (config.Desktops.TryGetValue(desktopName, out var desktopProfile))
        {
            var recipeName = desktopProfile.Recipe ?? desktopName;
            var found = DesktopRecipes.Find(recipeName);
            if (found is null && recipeName != "custom")
            {
                return (null, $"--desktop {desktopName} names no recipe; the built-in ones are {DesktopRecipes.Names}");
            }

            if (found is null && desktopProfile.Command is null)
            {
                return (null, $"[desktops.{desktopName}] has recipe = \"custom\" and no command");
            }

            if (sshDestination is null && desktopProfile.Host is { } host)
            {
                var hostProfile = catalog.Find(host);
                sshDestination = hostProfile?.Ssh ?? host;
                basis = hostProfile ?? basis;
            }

            return (basis with
            {
                Name = ReferenceEquals(basis, sshProfile) || basis.Name != desktopName ? $"{desktopName}@{basis.Name}" : desktopName,
                Ssh = sshDestination ?? string.Empty,
                Desktop = recipeName,
                Command = desktopProfile.Command,
                Gpu = desktopProfile.Gpu ?? basis.Gpu,
                Video = desktopProfile.Video ?? basis.Video,
                DesktopSize = desktopProfile.Size,
                DesktopEnv = desktopProfile.Env,
            }, null);
        }

        if (DesktopRecipes.Find(desktopName) is null)
        {
            return (null, $"--desktop {desktopName} names no recipe; the built-in ones are {DesktopRecipes.Names}");
        }

        overrides = overrides with { Desktop = desktopName };
        return (basis with
        {
            Name = sshProfile is null ? desktopName : $"{desktopName}@{sshProfile.Name}",
            Command = null,
            Desktop = desktopName,
            DesktopSize = null,
            DesktopEnv = null,
        }, null);
    }

    internal static (int Width, int Height)? ParseSize(string? text)
    {
        if (text is null)
        {
            return null;
        }

        var parts = text.Split('x', 'X');
        return parts.Length == 2
            && int.TryParse(parts[0], out var width)
            && int.TryParse(parts[1], out var height)
            && width > 0
            && height > 0
            ? (width, height)
            : null;
    }

    internal static string? NestedShellProblem(ShellMode shell, string? desktop, string? listen)
    {
        if (shell != ShellMode.Nested)
        {
            return null;
        }

        if (desktop is not null)
        {
            return $"--shell nested manages the windows itself and --desktop hands the screen to {desktop}";
        }

        return listen is not null
            ? "--shell nested manages the windows itself and --waypipe-listen hands them to whoever started the channel"
            : null;
    }

    internal static IReadOnlyList<SessionSettings> Autoconnect(
        SessionCatalog catalog, IReadOnlyList<SessionSettings> already, Config config, string audioFormat, BasinLogger log)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(already);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(audioFormat);
        var initial = new List<SessionSettings>(already);
        foreach (var profile in catalog.Profiles)
        {
            if (!profile.Autoconnect || initial.Any(settings => settings.Name == profile.Name))
            {
                continue;
            }

            var resolved = SessionSettings.Resolve(profile, new SessionOverrides(AudioFormat: audioFormat), config, log);
            if (resolved.Settings is { } settings)
            {
                if (initial.Any(static other => other.IsDesktop) && settings.IsDesktop)
                {
                    log.Warn($"session {profile.Name} autoconnects a desktop and another desktop is already starting, skipping it");
                    continue;
                }

                initial.Add(settings);
            }
            else
            {
                log.Warn($"session {profile.Name} cannot autoconnect: {resolved.Error}");
            }
        }

        return initial;
    }

    internal static string? LocalDesktopProblem(HostCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        return capabilities.LocalDesktops
            ? null
            : "--desktop with no --ssh runs the session on this machine, which needs Linux";
    }

    internal static string? LocalCommandProblem(HostCapabilities capabilities, string? command)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        return command is null || capabilities.LocalCommands
            ? null
            : "a trailing command runs a local Wayland client, which needs Linux; --ssh HOST runs it on a remote";
    }
}
