using Waylonia.Agent;
using Waylonia.Cli;
using static Waylonia.WayloniaLog;

namespace Waylonia;

internal static partial class DesktopHead
{
    private static partial int PlatformRunVisible(AgentProfile profile, AgentRequest request)
    {
        if (!OperatingSystem.IsLinux())
        {
            return CliRefusal.Write("--agent runs local Linux applications for an agent, which needs Linux");
        }

        using var runtime = new AgentRuntime(profile, headless: false);
        if (!runtime.Prepare(out var error))
        {
            return CliRefusal.Write(error ?? string.Empty);
        }

        DesktopWayloniaApp.PreparedAgent = runtime;
        try
        {
            return RunVisible(profile, request);
        }
        finally
        {
            DesktopWayloniaApp.PreparedAgent = null;
        }
    }

    private static partial int PlatformRunHeadless(AgentProfile profile, AgentRequest request)
    {
        if (!OperatingSystem.IsLinux())
        {
            return CliRefusal.Write("--agent runs local Linux applications for an agent, which needs Linux");
        }

        using var runtime = new AgentRuntime(profile, headless: true);
        if (!runtime.Prepare(out var error))
        {
            return CliRefusal.Write(error ?? string.Empty);
        }

        var icons = new IconCache(request.Paths.IconCacheRoot);
        var linux = new LinuxXWayland();
        var xwayland = request.Capabilities.XWayland && request.Config.Host.XWayland
            ? new AgentXWayland(
                () => linux.TryCreateModule(shellAdopts: true),
                (module, shell) => linux.AttachShell(module, shell, icons, Log),
                linux.DisplayName)
            : null;
        using var host = new AgentHeadlessHost(runtime, request.Config.ShellSettings, request.SocketName, xwayland);
        return host.Run();
    }
}
