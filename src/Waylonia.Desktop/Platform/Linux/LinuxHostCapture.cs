using Basin.Avalonia;
using Basin.Hosted;
using Basin.XWayland;
using static Waylonia.WayloniaLog;

namespace Waylonia;

internal sealed class LinuxHostCapture : IHostCapture, IDisposable
{
    private readonly BasinOutputView? _view;
    private X11KeyboardGrab? _grab;
    private bool _disposed;

    private LinuxHostCapture(BasinOutputView? view) => _view = view;

    public LinuxHostCapture()
        : this(null)
    {
    }

    public IDisposable? TryGrab(
        global::Avalonia.Controls.TopLevel anchor,
        BasinOutputView view,
        BasinCompositorHost host,
        CaptureHooks hooks)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(hooks);
        var display = HostSession.Display;
        if (!string.IsNullOrEmpty(HostSession.WaylandDisplay) || string.IsNullOrEmpty(display))
        {
            Log.Warn($"capture: the Wayland host keeps Super and its own bindings, no client protocol can take them");
            return null;
        }

        var instance = new LinuxHostCapture(view);
        view.Post(() =>
        {
            if (instance._disposed)
            {
                return;
            }

            instance._grab = X11KeyboardGrab.TryGrab(host.Loop, display, (code, pressed) =>
                global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (!hooks.Filter(code, pressed))
                    {
                        hooks.Inject(code, pressed);
                    }
                }));
            if (instance._grab is null)
            {
                Log.Warn($"capture: the X keyboard grab failed, the host keeps its own bindings");
            }
            else
            {
                Log.Debug($"capture: X keyboard grabbed on {display}, every key goes to the desktop");
            }
        });
        return instance;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _view?.Post(() =>
        {
            _grab?.Dispose();
            _grab = null;
        });
    }
}
