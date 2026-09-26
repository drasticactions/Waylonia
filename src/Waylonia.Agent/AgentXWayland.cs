using Basin;
using Basin.Hosted;
using Basin.Shell.Nested;

namespace Waylonia.Agent;

internal sealed record AgentXWayland(
    Func<IProtocolModule?> Create,
    Action<IProtocolModule, NestedShell> AttachShell,
    Func<BasinCompositorHost, string?> DisplayName);
