using Avalonia.Controls;
using Basin.Avalonia;
using Basin.Hosted;

namespace Waylonia;

internal interface IGlobalHotkeys
{
    IDisposable? TryStart(
        IReadOnlyList<Hotkey> hotkeys,
        TopLevel anchor,
        BasinOutputView view,
        BasinCompositorHost host,
        Action<Hotkey> launch);
}
