using Avalonia.Controls;
using Basin.Avalonia;
using Basin.Hosted;
using static Waylonia.WayloniaLog;

namespace Waylonia;

internal sealed class NullGlobalHotkeys : IGlobalHotkeys
{
    public static readonly NullGlobalHotkeys Instance = new();

    public IDisposable? TryStart(
        IReadOnlyList<Hotkey> hotkeys,
        TopLevel anchor,
        BasinOutputView view,
        BasinCompositorHost host,
        Action<Hotkey> launch)
    {
        Log.Warn($"global hotkeys are not implemented on this host, the [hotkeys] table is ignored");
        return null;
    }
}
