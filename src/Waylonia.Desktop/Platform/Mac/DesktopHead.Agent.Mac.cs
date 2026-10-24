using Waylonia.Agent;
using Waylonia.Cli;

namespace Waylonia;

internal static partial class DesktopHead
{
    private static partial int PlatformRunVisible(AgentProfile profile, AgentRequest request)
    {
        return CliRefusal.Write("--agent runs local Linux applications for an agent, which needs Linux");
    }

    private static partial int PlatformRunHeadless(AgentProfile profile, AgentRequest request)
    {
        return CliRefusal.Write("--agent runs local Linux applications for an agent, which needs Linux");
    }
}
