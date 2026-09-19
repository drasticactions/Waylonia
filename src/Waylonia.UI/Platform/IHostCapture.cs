using Avalonia.Controls;
using Basin.Avalonia;
using Basin.Hosted;

namespace Waylonia;

internal interface IHostCapture
{
    IDisposable? TryGrab(TopLevel anchor, BasinOutputView view, BasinCompositorHost host, CaptureHooks hooks);
}
