using Basin.Diagnostics;

namespace Waylonia.Agent;

internal static class AgentLog
{
    internal static readonly BasinLogger Log = BasinLog.For("agent");
}
