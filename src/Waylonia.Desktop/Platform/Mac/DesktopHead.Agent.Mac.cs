using Waylonia.Agent;
using static Waylonia.WayloniaLog;

namespace Waylonia;

internal static partial class DesktopHead
{
    private static partial int PlatformRunVisible(AgentProfile profile, AgentRequest request)
    {
        Log.Error($"--agent runs local Linux applications for an agent, which needs Linux");
        return 1;
    }

    private static partial int PlatformRunHeadless(AgentProfile profile, AgentRequest request)
    {
        Log.Error($"--agent runs local Linux applications for an agent, which needs Linux");
        return 1;
    }
}
