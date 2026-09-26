using Waylonia.Accessibility;

namespace Waylonia.Agent;

internal sealed record AgentWindowRow(
    ulong Id, int Pid, long? Launch, bool X11, bool Dialog, ulong Parent, A11yWindowTarget Target, bool Accessible);
