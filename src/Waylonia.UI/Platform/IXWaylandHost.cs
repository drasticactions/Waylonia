using Basin;
using Basin.Avalonia;
using Basin.Diagnostics;
using Basin.Hosted;
using Basin.Shell.Nested;
using Waylonia.Shell;

namespace Waylonia;

internal interface IXWaylandHost
{
    IProtocolModule? TryCreateModule();

    void Attach(IProtocolModule module, BasinCompositorHost host, ToplevelWindows windows);

    void AttachShell(IProtocolModule module, NestedShell shell, IconCache icons, BasinLogger log);

    string? DisplayName(BasinCompositorHost host);
}
