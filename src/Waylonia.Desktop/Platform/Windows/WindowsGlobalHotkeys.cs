using Avalonia.Controls;
using Basin.Avalonia;
using Basin.Hosted;

namespace Waylonia;

internal sealed class WindowsGlobalHotkeys : IGlobalHotkeys
{
    public IDisposable? TryStart(
        IReadOnlyList<Hotkey> hotkeys,
        TopLevel anchor,
        BasinOutputView view,
        BasinCompositorHost host,
        Action<Hotkey> launch) => Win32Hotkeys.TryStart(hotkeys, anchor, launch);
}
