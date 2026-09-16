using Basin;
using Basin.Avalonia;
using Basin.Diagnostics;
using Waylonia.Shell;

namespace Waylonia;

internal static class WayloniaXWayland
{
    public static IProtocolModule? TryCreateModule() => null;

    public static void Attach(IProtocolModule module, BasinCompositorHost host, ToplevelWindows windows)
    {
    }

    public static void AttachShell(IProtocolModule module, NestedShell shell, IconCache icons, BasinLogger log)
    {
    }

    public static string? DisplayName(BasinCompositorHost host) => null;
}
