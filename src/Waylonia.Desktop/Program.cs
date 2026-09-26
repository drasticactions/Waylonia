using System.CommandLine;
using Basin.Diagnostics;
using Waylonia.Agent;
using Waylonia.Cli;
using Waylonia.Sessions;
using Waylonia.Shell;
using Waylonia.UI;

namespace Waylonia;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
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
        var shellOption = cli.Add(new Option<string?>("--shell")
        {
            Description = "windows opens one host window per client window; nested holds every client window " +
                "inside one Waylonia-managed desktop with frames and a panel. Overrides [host] shell for this run.",
            HelpName = "windows|nested",
        });
        shellOption.Validators.Add(result =>
        {
            if (result.GetValueOrDefault<string?>() is { } text && ShellModes.Parse(text) is null)
            {
                result.AddError($"--shell takes {ShellModes.Names}");
            }
        });
        var virtualInputOption = cli.Add(new Option<bool>("--virtual-input")
        {
            Description = "offer the virtual keyboard and pointer protocols, which let any client type and click into every other one. Overrides [host] virtual-input for this run.",
        });
        var agentOption = cli.Add(new Option<string?>("--agent")
        {
            Description = "run a compositor that holds only an AI agent's applications, under the profile NAME in " +
                "$XDG_STATE_HOME/waylonia/agents (default when NAME is left out). An MCP host reaches it through " +
                "basin-mcp --launch -- waylonia --agent NAME.",
            HelpName = "NAME",
            Arity = ArgumentArity.ZeroOrOne,
        });
        var headlessOption = cli.Add(new Option<bool>("--headless")
        {
            Description = "with --agent, run with no window at all.",
        });
        var sizeOption = cli.Add(new Option<string?>("--size")
        {
            Description = "with --agent, the fixed output size, WxH. Overrides size in agent.toml.",
            HelpName = "WxH",
        });
        var scaleOption = cli.Add(new Option<double?>("--scale")
        {
            Description = "with --agent --headless, the output scale. Overrides scale in agent.toml.",
            HelpName = "SCALE",
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
            SessionSettings.CreateDecoder = DesktopHead.Video.Create;
            var configValue = result.GetValue(configOption);
            var capabilities = DesktopHead.Capabilities;
            var paths = configValue is null ? DesktopHead.Paths() : DesktopHead.Paths().WithConfig(configValue == "false" ? null : configValue);
            var config = Config.Load(paths, log);
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
            var agentResult = result.GetResult(agentOption);
            var headless = result.GetValue(headlessOption);
            var agentSize = result.GetValue(sizeOption);
            var agentScale = result.GetValue(scaleOption);
            if (agentResult is { Implicit: false })
            {
                var explicitShell = result.GetValue(shellOption) is { } agentShellText ? ShellModes.Parse(agentShellText) : null;
                var trailing = result.GetValue(command) is { Length: > 0 } agentParts ? string.Join(' ', agentParts) : null;
                if (RunRules.AgentProblem(capabilities, ssh, desktopName, listen, trailing, explicitShell) is { } agentProblem)
                {
                    log.Error($"{agentProblem}");
                    return 1;
                }

                var agentStatus = DesktopHead.RunAgent(new AgentRequest(
                    result.GetValue(agentOption) ?? AgentProfile.DefaultName,
                    headless,
                    agentSize,
                    agentScale,
                    capabilities,
                    paths,
                    config,
                    store,
                    result.GetValue(socketOption) ?? config.Socket,
                    result.GetValue(frames),
                    result.GetValue(screenshot)));
                cli.ReportFrames(WayloniaApp.Rendered);
                return agentStatus;
            }

            if (headless || agentSize is not null || agentScale is not null)
            {
                log.Error($"--headless, --size and --scale shape an --agent run, and this run has no --agent");
                return 1;
            }

            var shell = result.GetValue(shellOption) is { } shellText ? ShellModes.Parse(shellText)!.Value : config.Host.Shell;
            var host = config.Host with { Shell = shell, VirtualInput = config.Host.VirtualInput || result.GetValue(virtualInputOption) };

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

            if (RunRules.NestedShellProblem(shell, desktopName, listen) is { } shellProblem)
            {
                log.Error($"{shellProblem}");
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
                var (profile, error) = RunRules.DesktopProfileFor(desktopName, ssh, catalog, config, ref overrides);
                if (error is not null)
                {
                    log.Error($"{error}");
                    return 1;
                }

                if (profile!.Ssh.Length == 0)
                {
                    if (RunRules.LocalDesktopProblem(capabilities) is { } desktopProblem)
                    {
                        log.Error($"{desktopProblem}");
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

            if (RunRules.LocalCommandProblem(capabilities, commandText) is { } problem)
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
            IReadOnlyList<SessionSettings> starting = initial;
            if (commandText is null && listen is null && localDesktop is null)
            {
                starting = RunRules.Autoconnect(catalog, initial, config, audioFormat, log);
            }

            var status = DesktopHead.Run(new WayloniaRun(
                capabilities,
                paths,
                host,
                starting,
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
}
