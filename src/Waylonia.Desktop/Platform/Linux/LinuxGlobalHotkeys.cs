using Avalonia.Threading;
using Basin.Avalonia;
using Basin.Hosted;
using Basin.Diagnostics;
using Basin.XWayland;
using static Waylonia.WayloniaLog;

namespace Waylonia;

internal sealed class LinuxGlobalHotkeys : IGlobalHotkeys
{
    public IDisposable? TryStart(
        IReadOnlyList<Hotkey> hotkeys,
        global::Avalonia.Controls.TopLevel anchor,
        BasinOutputView view,
        BasinCompositorHost host,
        Action<Hotkey> launch)
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
        {
            return PortalGlobalHotkeys.Start(hotkeys, launch);
        }

        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
        {
            return X11GlobalHotkeys.Start(hotkeys, view, host, launch);
        }

        Log.Warn($"this host has neither WAYLAND_DISPLAY nor DISPLAY, global hotkeys are off");
        return null;
    }
}
