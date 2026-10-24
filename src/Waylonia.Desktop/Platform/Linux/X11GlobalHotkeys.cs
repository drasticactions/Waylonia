using Avalonia.Threading;
using Basin.Avalonia;
using Basin.Hosted;
using Basin.Diagnostics;
using Basin.Shell.Nested;
using Basin.XWayland;
using static Waylonia.WayloniaLog;

namespace Waylonia;

internal sealed class X11GlobalHotkeys : IDisposable
{
    private readonly BasinOutputView _view;

    private X11HotkeyGrabber? _grabber;

    private X11GlobalHotkeys(BasinOutputView view) => _view = view;

    public static X11GlobalHotkeys Start(
        IReadOnlyList<Hotkey> hotkeys, BasinOutputView view, BasinCompositorHost host, Action<Hotkey> launch)
    {
        var instance = new X11GlobalHotkeys(view);
        var display = Environment.GetEnvironmentVariable("DISPLAY");
        view.Post(() =>
        {
            var grabber = X11HotkeyGrabber.TryConnect(host.Loop, display);
            if (grabber is null)
            {
                Log.Warn($"hotkeys: off, the X server did not accept a connection");
                return;
            }

            var registered = 0;
            foreach (var hotkey in hotkeys)
            {
                var captured = hotkey;
                if (grabber.TryGrab(
                        Modifiers(hotkey.Modifiers), hotkey.Key,
                        () => Dispatcher.UIThread.Post(() => launch(captured))))
                {
                    registered++;
                }
                else
                {
                    Log.Warn($"hotkeys: {hotkey.Chord} skipped, the X server refused the grab");
                }
            }

            if (registered == 0)
            {
                grabber.Dispose();
                return;
            }

            instance._grabber = grabber;
            Log.Debug($"hotkeys: {registered} registered with the X server");
        });
        return instance;
    }

    public void Dispose() => _view.Post(() =>
    {
        _grabber?.Dispose();
        _grabber = null;
    });

    private static X11HotkeyModifiers Modifiers(ShellModifiers modifiers)
    {
        var value = X11HotkeyModifiers.None;
        if ((modifiers & ShellModifiers.Shift) != 0)
        {
            value |= X11HotkeyModifiers.Shift;
        }

        if ((modifiers & ShellModifiers.Ctrl) != 0)
        {
            value |= X11HotkeyModifiers.Ctrl;
        }

        if ((modifiers & ShellModifiers.Alt) != 0)
        {
            value |= X11HotkeyModifiers.Alt;
        }

        if ((modifiers & ShellModifiers.Super) != 0)
        {
            value |= X11HotkeyModifiers.Super;
        }

        return value;
    }
}
