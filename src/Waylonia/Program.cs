using System.CommandLine;
using Basin.Diagnostics;
using Waylonia.Cli;
using Waylonia.Sessions;
using Waylonia.Ui;

namespace Waylonia;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (AskPass.IsAskPassRun(args, Environment.GetEnvironmentVariable(AskPass.Variable)))
        {
            return AskPass.Run(AskPass.PromptOf(args));
        }

        HostSession.Capture();
        var cli = new WayloniaCommand("Runs remote Wayland sessions on this desktop through waypipe over ssh.");
        var frames = cli.Add(CliOptions.Frames());
        var screenshot = cli.Add(CliOptions.Screenshot());
        frames.Hidden = true;
        screenshot.Hidden = true;
        var socketOption = cli.Add(new Option<string?>("--socket")
        {
            Description = "the Wayland socket name to bind, where the platform has one.",
        });
        var listenOption = cli.Add(CliOptions.WaypipeListen());
        var sshOption = cli.Add(new Option<string?>("--ssh")
        {
            Description = "connect this session at start. A saved session in sessions/NAME.toml matches first; " +
                "otherwise an ssh destination, connected with the flags below and the config defaults.",
            HelpName = "NAME|DESTINATION",
        });
        var configOption = cli.Add(new Option<string?>("--config")
        {
            Description = "read this file instead of ~/.config/waylonia/waylonia.toml, with sessions/ beside it; false skips both.",
        });
        var migrateOption = cli.Add(new Option<bool>("--migrate-hosts")
        {
            Description = "write every [hosts.NAME] profile of the config file to sessions/NAME.toml and exit.",
            Hidden = true,
        });
        var settingsOption = cli.Add(new Option<bool>("--settings")
        {
            Description = "open the settings window at start.",
            Hidden = true,
        });
        var gpuOption = cli.Add(CliOptions.Gpu());
        var audioOption = cli.Add(new Option<bool>("--audio")
        {
            Description = "play the remote session's sound on this host. Off by default.",
        });
        var audioFormatOption = cli.Add(new Option<string>("--audio-format")
        {
            Description = "the format the remote session's sound is sent in: f32 or s16.",
            HelpName = "NAME",
            DefaultValueFactory = _ => "f32",
            Hidden = true,
        });
        audioFormatOption.Validators.Add(result =>
        {
            if (result.GetValueOrDefault<string>() is not ("f32" or "s16"))
            {
                result.AddError("--audio-format takes f32 or s16");
            }
        });
        var desktopOption = cli.Add(new Option<string?>("--desktop")
        {
            Description = "run a whole Linux desktop in one window. A saved session with desktop set matches " +
                "first, then a [desktops.NAME] profile in the config file, then a built-in recipe: " +
                $"{DesktopRecipes.Names}.",
            HelpName = "NAME",
        });
        var desktopSizeOption = cli.Add(new Option<string?>("--desktop-size")
        {
            Description = "the screen window's initial size, WxH. The default is 80% of its screen.",
            HelpName = "WxH",
        });
        var videoOption = cli.Add(CliOptions.Video());
        var compressOption = cli.Add(CliOptions.Compress());
        var command = new Argument<string[]>("command")
        {
            Description = "run this program as a local Wayland client, which needs Linux; with --ssh or --desktop, run it on the remote instead.",
            Arity = ArgumentArity.ZeroOrMore,
        };
        cli.Command.Arguments.Add(command);

        return cli.Run(args, result =>
        {
            cli.ConfigureLogging(result);
            var log = BasinLog.For("waylonia");
            var configValue = result.GetValue(configOption);
            var config = Config.Load(
                configValue == "false",
                configValue == "false" ? null : configValue,
                log);
            var store = new SessionStore(config.SessionsDirectory);
            if (result.GetValue(migrateOption))
            {
                return MigrateHosts(config, store, log);
            }

            var catalog = store.Load(log);
            var commandText = result.GetValue(command) is { Length: > 0 } parts ? string.Join(' ', parts) : null;
            var listen = result.GetValue(listenOption);
            var ssh = result.GetValue(sshOption);
            var desktopName = result.GetValue(desktopOption);
            var desktopSize = result.GetValue(desktopSizeOption);

            if (desktopName is not null && commandText is not null)
            {
                log.Error($"--desktop is the command; a trailing command runs beside it");
                return 1;
            }

            if (listen is not null && ssh is not null)
            {
                log.Error($"--waypipe-listen accepts a channel and --ssh opens its own");
                return 1;
            }

            if (desktopName is not null && listen is not null)
            {
                log.Error($"--desktop starts the session and --waypipe-listen waits for one someone else started");
                return 1;
            }

            if (listen is not null && commandText is not null)
            {
                log.Error($"a trailing command spawns a local client and --waypipe-listen waits for a remote one");
                return 1;
            }

            var explicitCompress = result.GetResult(compressOption) is null or { Implicit: true }
                ? null
                : result.GetValue(compressOption);
            var explicitGpu = result.GetResult(gpuOption) is null or { Implicit: true }
                ? (bool?)null
                : result.GetValue(gpuOption);
            var explicitAudio = result.GetResult(audioOption) is null or { Implicit: true }
                ? (bool?)null
                : result.GetValue(audioOption);
            var explicitVideo = result.GetResult(videoOption) is null or { Implicit: true }
                ? null
                : result.GetValue(videoOption);
            var audioFormat = result.GetValue(audioFormatOption)!;
            var overrides = new SessionOverrides(
                explicitCompress, explicitGpu, explicitAudio, explicitVideo,
                Command: null, Desktop: null, desktopSize, audioFormat);

            var initial = new List<SessionSettings>();
            LocalDesktop? localDesktop = null;
            SessionProfile? adHoc = null;
            if (desktopName is not null)
            {
                var (profile, error) = DesktopProfileFor(desktopName, ssh, catalog, config, ref overrides);
                if (error is not null)
                {
                    log.Error($"{error}");
                    return 1;
                }

                if (profile!.Ssh.Length == 0)
                {
                    if (!OperatingSystem.IsLinux())
                    {
                        log.Error($"--desktop with no --ssh runs the session on this machine, which needs Linux");
                        return 1;
                    }

                    var resolved = SessionSettings.Resolve(profile, overrides, config, log, adHoc: true);
                    if (resolved.Settings is not { } local)
                    {
                        log.Error($"{resolved.Error}");
                        return 1;
                    }

                    localDesktop = new LocalDesktop(local.Desktop!, local.DesktopEnv, local.DesktopSize, local.Gpu);
                }
                else
                {
                    adHoc = profile;
                }
            }
            else if (ssh is not null)
            {
                adHoc = catalog.Find(ssh) ?? new SessionProfile(ssh, ssh);
                if (commandText is not null)
                {
                    overrides = overrides with { Command = commandText };
                }

                commandText = null;
            }
            else if (desktopSize is not null)
            {
                log.Error($"--desktop-size sizes the window --desktop opens");
                return 1;
            }

            if (adHoc is not null)
            {
                var resolved = SessionSettings.Resolve(adHoc, overrides, config, log, adHoc: true);
                if (resolved.Settings is not { } settings)
                {
                    log.Error($"{resolved.Error}");
                    return 1;
                }

                if (explicitAudio == true && !Audio.WayloniaAudio.Wanted(true, settings.Ssh, listen))
                {
                    settings = settings with { Audio = false };
                }

                initial.Add(settings);
            }

            if (ssh is null && listen is null && desktopName is null)
            {
                commandText ??= config.Command;
            }

            if (LocalCommandProblem(OperatingSystem.IsLinux(), commandText) is { } problem)
            {
                log.Error($"{problem}");
                return 1;
            }

            ListenSettings? listenSettings = null;
            if (listen is not null)
            {
                if (explicitAudio == true)
                {
                    Audio.WayloniaAudio.Wanted(true, null, listen);
                }

                var resolved = SessionSettings.Resolve(new SessionProfile("listen", listen), overrides, config, log, adHoc: true);
                if (resolved.Settings is not { } settings)
                {
                    log.Error($"{resolved.Error}");
                    return 1;
                }

                listenSettings = new ListenSettings(listen, settings.Compression, settings.Gpu, settings.Video, settings.VideoDecoder);
            }
            else if (explicitAudio == true && adHoc is null && commandText is not null)
            {
                Audio.WayloniaAudio.Wanted(true, null, null);
            }

            var manager = commandText is null && listen is null && localDesktop is null && adHoc is null;
            if (commandText is null && listen is null && localDesktop is null)
            {
                foreach (var profile in catalog.Profiles)
                {
                    if (!profile.Autoconnect || initial.Any(settings => settings.Name == profile.Name))
                    {
                        continue;
                    }

                    var resolved = SessionSettings.Resolve(
                        profile, new SessionOverrides(AudioFormat: audioFormat), config, log);
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
            }

            var status = WayloniaApp.Run(new WayloniaRun(
                config.Host,
                initial,
                manager,
                commandText,
                localDesktop,
                listenSettings,
                result.GetValue(frames),
                result.GetValue(screenshot),
                result.GetValue(socketOption) ?? config.Socket,
                config,
                store,
                audioFormat,
                result.GetValue(settingsOption)));
            cli.ReportFrames(WayloniaApp.Rendered);
            return status;
        });
    }

    private static int MigrateHosts(Config config, SessionStore store, BasinLogger log)
    {
        if (store.Directory is null)
        {
            log.Error($"--migrate-hosts writes beside the config file and --config false skips it");
            return 1;
        }

        if (config.LegacyHosts.Count == 0)
        {
            log.Info($"{config.Path} has no [hosts.NAME] profile to migrate");
            return 0;
        }

        foreach (var profile in config.LegacyHosts)
        {
            var path = store.PathOf(profile.Name)!;
            if (File.Exists(path))
            {
                log.Warn($"{path} exists already, leaving it alone");
                continue;
            }

            try
            {
                store.Save(profile);
                log.Info($"wrote {path}");
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                log.Error($"cannot write {path}: {error.Message}");
                return 1;
            }
        }

        log.Info($"remove the [hosts] section from {config.Path} once the sessions look right");
        return 0;
    }

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

    internal static string? LocalCommandProblem(bool linux, string? command) =>
        command is null || linux
            ? null
            : "a trailing command runs a local Wayland client, which needs Linux; --ssh HOST runs it on a remote";
}
