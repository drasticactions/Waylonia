using Waylonia.Agent;
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
            Log.Error($"{error}");
            return 1;
        }

        if (request.Size is { } sizeText)
        {
            if (RunRules.ParseSize(sizeText) is not { } size)
            {
                Log.Error($"--size takes WxH, such as 1280x800");
                return 1;
            }

            profile = profile with { Width = size.Width, Height = size.Height };
        }

        if (request.Scale is { } scale)
        {
            if (scale is < 0.5 or > 4)
            {
                Log.Error($"--scale takes 0.5 to 4");
                return 1;
            }

            profile = profile with { Scale = scale };
        }

        var headless = request.Headless || profile.Headless;
        Log.Info($"agent profile {profile.Name} in {profile.Directory}: {profile.LaunchSummary}");
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
