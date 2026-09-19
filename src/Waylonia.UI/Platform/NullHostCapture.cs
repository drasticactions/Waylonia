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
        Log.Warn($"this host cannot be grabbed, its own chords stay with it while the desktop is captured");
        return null;
    }
}
