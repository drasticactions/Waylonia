using Waylonia.Agent;
using Waylonia.Cli;
using static Waylonia.WayloniaLog;

namespace Waylonia;

internal static partial class DesktopHead
{
    public static int RunAgent(AgentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var root = AgentProfileStore.RootFor(request.Paths);
        if (AgentProfileStore.Load(root, request.Name, Log, out var error) is not { } profile)
        {
            return CliRefusal.Write(error ?? string.Empty);
        }

        if (request.Size is { } sizeText)
        {
            if (RunRules.ParseSize(sizeText) is not { } size)
            {
                return CliRefusal.Write("--size takes WxH, such as 1280x800");
            }

            profile = profile with { Width = size.Width, Height = size.Height };
        }

        if (request.Scale is { } scale)
        {
            if (scale is < 0.5 or > 4)
            {
                return CliRefusal.Write("--scale takes a number from 0.5 to 4");
            }

            profile = profile with { Scale = scale };
        }

        var headless = request.Headless || profile.Headless;
        Log.Info($"agent {profile.Name}: profile in {profile.Directory}, {profile.LaunchSummary}");
        if (headless)
        {
            return PlatformRunHeadless(profile, request);
        }

        return PlatformRunVisible(profile, request);
    }

    private static int RunVisible(AgentProfile profile, AgentRequest request)
    {
        var config = request.Config;
        var run = new WayloniaRun(
            request.Capabilities,
            request.Paths,
            config.Host with { Shell = Shell.ShellMode.Nested, Tray = false, TrayApps = false, Clipboard = false, VirtualInput = false },
            [],
            false,
            null,
            null,
            null,
            request.Frames,
            request.Screenshot,
            request.SocketName,
            config,
            request.Store,
            Agent: profile,
            Headless: false);
        return Run(run);
    }

    private static partial int PlatformRunHeadless(AgentProfile profile, AgentRequest request);

    private static partial int PlatformRunVisible(AgentProfile profile, AgentRequest request);
}
