using Basin.Avalonia;
using Basin.Hosted;
using Basin.Shell.Nested;
using static Waylonia.WayloniaLog;

namespace Waylonia;

internal sealed class CaptureToggle : IDisposable
{
    private readonly CaptureChord _chord;
    private readonly BasinOutputView _view;
    private readonly BasinCompositorHost _host;
    private readonly IHostCapture _capture;
    private readonly Action<bool> _hotkeys;
    private readonly CaptureHooks _hooks;

    private ICaptureTarget? _target;
    private global::Avalonia.Controls.Window? _window;
    private Action<string>? _overrideTitle;
    private string _title = string.Empty;
    private IDisposable? _grab;
    private long _lastTap;
    private bool _tapIsAlone;
    private ShellModifiers _held;
    private bool _disposed;

    public CaptureToggle(
        CaptureChord chord,
        BasinOutputView view,
        BasinCompositorHost host,
        IHostCapture capture,
        Action<bool> hotkeys)
    {
        ArgumentNullException.ThrowIfNull(capture);
        _chord = chord;
        _view = view;
        _host = host;
        _capture = capture;
        _hotkeys = hotkeys;
        _hooks = new CaptureHooks(OnKey, (code, pressed) => _target?.InjectKey(code, pressed));
    }

    public bool Captured { get; private set; }

    public void Attach(ToplevelWindow window, string title) =>
        Attach(window, window, title, window.OverrideTitle);

    public void Attach(ICaptureTarget target, global::Avalonia.Controls.Window window, string title, Action<string> overrideTitle)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(window);
        Detach();
        _target = target;
        _window = window;
        _overrideTitle = overrideTitle;
        _title = title;
        _held = ShellModifiers.None;
        _lastTap = 0;
        target.KeyFilter = OnKey;
        window.Deactivated += OnDeactivated;
        Log.Info($"the desktop takes this host's keyboard on {_chord.Text}");
    }

    public void Detach()
    {
        if (_target is not { } target || _window is not { } window)
        {
            return;
        }

        Release();
        target.KeyFilter = null;
        window.Deactivated -= OnDeactivated;
        _target = null;
        _window = null;
        _overrideTitle = null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Detach();
    }

    private void OnDeactivated(object? sender, EventArgs e) => Release();

    private bool OnKey(uint code, bool pressed)
    {
        if (code == _chord.Code)
        {
            if (_chord.DoubleTap)
            {
                if (pressed)
                {
                    _tapIsAlone = true;
                }
                else if (_tapIsAlone && _lastTap != 0 && Environment.TickCount64 - _lastTap <= CaptureChord.DoubleTapMillis)
                {
                    _lastTap = 0;
                    Toggle();
                }
                else
                {
                    _lastTap = Environment.TickCount64;
                }
            }
            else if (pressed && _held == _chord.Modifiers)
            {
                Toggle();
            }

            return true;
        }

        if (CaptureChord.ModifierOf(code) is var modifier && modifier != ShellModifiers.None)
        {
            if (pressed)
            {
                _held |= modifier;
            }
            else
            {
                _held &= ~modifier;
            }
        }

        if (pressed)
        {
            _tapIsAlone = false;
        }

        return false;
    }

    private void Toggle()
    {
        if (Captured)
        {
            Release();
            return;
        }

        Captured = true;
        _hotkeys(false);
        _target?.CaptureInput(true);
        _grab = _capture.TryGrab(_window!, _view, _host, _hooks);
        _overrideTitle?.Invoke($"{_title} — captured, {_chord.Text} to release");
        Log.Info($"the desktop has this host's keyboard; use {_chord.Text} to release");
    }

    private void Release()
    {
        if (!Captured)
        {
            return;
        }

        Captured = false;
        _grab?.Dispose();
        _grab = null;
        _target?.CaptureInput(false);
        _hotkeys(true);
        _overrideTitle?.Invoke(_title);
        Log.Info($"the host has its keyboard back");
    }
}
