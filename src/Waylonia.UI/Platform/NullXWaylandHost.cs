using Basin;
using Basin.Avalonia;
using Basin.Diagnostics;
using Basin.Hosted;
using Waylonia.Shell;

namespace Waylonia;

internal sealed class NullXWaylandHost : IXWaylandHost
{
    public static readonly NullXWaylandHost Instance = new();

    public IProtocolModule? TryCreateModule() => null;

    public void Attach(IProtocolModule module, BasinCompositorHost host, ToplevelWindows windows)
    {
    }

    public void AttachShell(IProtocolModule module, NestedShell shell, IconCache icons, BasinLogger log)
    {
    }

    public string? DisplayName(BasinCompositorHost host) => null;
}
