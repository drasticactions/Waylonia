using Avalonia.Controls;
using Basin.Avalonia;
using Basin.Hosted;
using static Waylonia.WayloniaLog;

namespace Waylonia;

internal sealed class NullHostCapture : IHostCapture
{
    public static readonly NullHostCapture Instance = new();

    public IDisposable? TryGrab(TopLevel anchor, BasinOutputView view, BasinCompositorHost host, CaptureHooks hooks)
    {
        Log.Warn($"capture: this host cannot be grabbed, its own shortcuts stay with it while the desktop has the keyboard");
        return null;
    }
}
