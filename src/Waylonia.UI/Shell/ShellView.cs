using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Input;
using Avalonia.Media;
using Basin.Avalonia;
using Basin.Hosted;

namespace Waylonia.Shell;

internal sealed class ShellView : UserControl
{
    private readonly Panel _stage = new();
    private BasinToplevelView? _toplevel;
    private TopLevel? _top;
    private IInsetsManager? _insets;
    private IInputPane? _inputPane;
    private Thickness _safeArea;
    private double _keyboardHeight;
    private bool _keyboardOpen;
    private bool _scaleSettled;

    public ShellView(BasinOutputView? output = null)
    {
        Background = Brushes.Black;
        if (output is not null)
        {
            output.IsHitTestVisible = false;
            _stage.Children.Add(output);
        }

        Content = _stage;
        _stage.SizeChanged += (_, _) => ReportSize();
    }

    public BasinToplevelView? Toplevel => _toplevel;

    public bool SoftKeyboardOpen => _keyboardOpen;

    public Action<bool>? SoftKeyboard { get; set; }

    public Thickness SafeArea => _safeArea;

    public double KeyboardHeight => _keyboardHeight;

    public event Action<int, int, double>? OutputResized;

    public event Action<bool>? SoftKeyboardChanged;

    public void AttachHost(BasinCompositorHost host, Func<BasinCompositorHost, BasinViewOutput> createView)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(createView);
        if (_toplevel is not null)
        {
            throw new InvalidOperationException("the shell view already holds a compositor view");
        }

        _toplevel = new BasinToplevelView(host, createView);
        _stage.Children.Add(_toplevel);
        _toplevel.Focus();
    }

    public (int Width, int Height, double Scale) OutputSize(int fallbackWidth, int fallbackHeight)
    {
        var scale = Scale;
        var bounds = _stage.Bounds;
        var width = bounds.Width > 0 ? bounds.Width : fallbackWidth;
        var height = bounds.Height > 0 ? bounds.Height : fallbackHeight;
        return (Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)), scale);
    }

    public void ApplyCursor(Cursor? cursor)
    {
        if (_toplevel is { } view)
        {
            view.Cursor = cursor ?? new Cursor(StandardCursorType.Arrow);
        }
    }

    public void FocusShell() => _toplevel?.Focus();

    public void NotifyActivated(bool active) => _toplevel?.NotifyActivated(active);

    public void ToggleSoftKeyboard()
    {
        _toplevel?.Focus();
        SoftKeyboard?.Invoke(!_keyboardOpen);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (TopLevel.GetTopLevel(this) is { } top)
        {
            _top = top;
            top.ScalingChanged += OnScalingChanged;
            _insets = top.InsetsManager;
            _inputPane = top.InputPane;
            Hook();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_top is { } top)
        {
            top.ScalingChanged -= OnScalingChanged;
            _top = null;
        }

        Unhook();
        _insets = null;
        _inputPane = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void Hook()
    {
        if (_insets is { } insets)
        {
            insets.SafeAreaChanged += OnSafeAreaChanged;
            _safeArea = insets.SafeAreaPadding;
        }

        if (_inputPane is { } pane)
        {
            pane.StateChanged += OnInputPaneChanged;
            ApplyInputPane(pane.State, pane.OccludedRect);
        }

        ApplyPadding();
    }

    private void Unhook()
    {
        if (_insets is { } insets)
        {
            insets.SafeAreaChanged -= OnSafeAreaChanged;
        }

        if (_inputPane is { } pane)
        {
            pane.StateChanged -= OnInputPaneChanged;
        }
    }

    private void OnScalingChanged(object? sender, EventArgs e)
    {
        _scaleSettled = true;
        ReportSize();
    }

    private void OnSafeAreaChanged(object? sender, SafeAreaChangedArgs e) => ApplySafeArea(e.SafeAreaPadding);

    private void OnInputPaneChanged(object? sender, InputPaneStateEventArgs e) => ApplyInputPane(e.NewState, e.EndRect);

    public void ApplySafeArea(Thickness padding)
    {
        _safeArea = padding;
        ApplyPadding();
    }

    public void ApplyInputPane(InputPaneState state, Rect occluded)
    {
        var open = state == InputPaneState.Open;
        var overlap = 0.0;
        if (open && occluded.Height > 0)
        {
            var bottom = _top is { } top && this.TranslatePoint(new Point(0, Bounds.Height), top) is { } point ? point.Y : Bounds.Height;
            overlap = Math.Clamp(bottom - occluded.Top, 0, Math.Max(0, Bounds.Height));
        }

        _keyboardHeight = overlap;
        ApplyPadding();
        if (open != _keyboardOpen)
        {
            _keyboardOpen = open;
            SoftKeyboardChanged?.Invoke(open);
        }
    }

    private void ApplyPadding() =>
        Padding = new Thickness(_safeArea.Left, _safeArea.Top, _safeArea.Right, Math.Max(_safeArea.Bottom, _keyboardHeight));

    private double Scale
    {
        get
        {
            var scaling = _top?.RenderScaling ?? 1.0;
            return scaling > 0 && (_scaleSettled || scaling != 1.0) ? scaling : 1.0;
        }
    }

    private void ReportSize()
    {
        var bounds = _stage.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var scale = Scale;
        OutputResized?.Invoke(
            Math.Max(1, (int)Math.Round(bounds.Width * scale)),
            Math.Max(1, (int)Math.Round(bounds.Height * scale)),
            scale);
    }
}
