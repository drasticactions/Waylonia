using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Basin.Avalonia;
using Basin.Hosted;
using BluerCurve.Chrome;

namespace Waylonia.Shell;

internal sealed class ShellWindow : BluerCurveWindow, IShellHost
{
    private readonly ShellWindowState _initial;
    private string _title = "Waylonia";
    private bool _allowClose;
    private DispatcherTimer? _saveTimer;
    private ShellWindowState _lastNormal = ShellWindowState.Default;
    private TextBlock? _agentStatus;
    private Button? _resume;
    private bool _fixedSize;

    public const int AgentStripHeight = 32;

    public ShellWindow(BasinCompositorHost host, ShellWindowState state, Func<BasinCompositorHost, BasinViewOutput> createView)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(createView);
        _initial = state;
        View = new ShellView();
        View.AttachHost(host, createView);
        Title = _title;
        Width = state.Width;
        Height = state.Height;
        MinWidth = 320;
        MinHeight = 240;
        Content = View;
        Background = global::Avalonia.Media.Brushes.Black;
        if (state.X is { } x && state.Y is { } y)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = new PixelPoint(x, y);
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        Opened += (_, _) =>
        {
            View.FocusShell();
            if (!FitsAScreen())
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
                if (Screens.ScreenFromPoint(Position) is null && Screens.Primary is { } primary)
                {
                    Position = CursorScreenPolicy.Centered(
                        primary.WorkingArea,
                        (int)Math.Round(Width * primary.Scaling),
                        (int)Math.Round(Height * primary.Scaling)) ?? Position;
                }
            }

            if (state.FullScreen && !_fixedSize)
            {
                WindowState = WindowState.FullScreen;
            }
        };
        Activated += (_, _) =>
        {
            View.FocusShell();
            View.NotifyActivated(true);
            ActivatedOnHost?.Invoke();
        };
        Deactivated += (_, _) =>
        {
            View.NotifyActivated(false);
            DeactivatedOnHost?.Invoke();
        };
        PositionChanged += (_, _) => ScheduleSave();
        SizeChanged += (_, _) => ScheduleSave();
        Closing += (_, e) =>
        {
            if (!_allowClose)
            {
                e.Cancel = true;
                CloseRequested?.Invoke();
            }
        };
    }

    public ShellView View { get; }

    public TopLevel? TopLevel => this;

    public bool CanFullScreen => !_fixedSize;

    public event Action? CloseRequested;

    public event Action? ActivatedOnHost;

    public event Action? DeactivatedOnHost;

    public event Action<ShellWindowState>? StateChanged;

    public bool IsFullScreen => WindowState == WindowState.FullScreen;

    public void ToggleFullScreen()
    {
        if (!_fixedSize)
        {
            WindowState = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;
        }
    }

    public void FixForAgent(int outputWidth, int outputHeight, Action resume)
    {
        ArgumentNullException.ThrowIfNull(resume);
        _fixedSize = true;
        _agentStatus = new TextBlock
        {
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
            Margin = new Thickness(8, 0),
        };
        _resume = new Button
        {
            Content = "Resume agent",
            IsVisible = false,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
            Margin = new Thickness(4, 0),
        };
        _resume.Click += (_, _) => resume();
        var strip = new DockPanel { Height = AgentStripHeight, LastChildFill = true };
        strip.Bind(Panel.BackgroundProperty, strip.GetResourceObservable("BcBgBrush"));
        _agentStatus.Bind(TextBlock.ForegroundProperty, _agentStatus.GetResourceObservable("BcFgBrush"));
        DockPanel.SetDock(_resume, global::Avalonia.Controls.Dock.Right);
        strip.Children.Add(_resume);
        strip.Children.Add(_agentStatus);
        var layout = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(strip, global::Avalonia.Controls.Dock.Top);
        Content = null;
        layout.Children.Add(strip);
        layout.Children.Add(View);
        Content = layout;
        CanResize = false;
        _agentOutput = new Size(outputWidth, outputHeight);
        SetFixedSize(outputWidth, outputHeight + AgentStripHeight);
        WindowState = WindowState.Normal;
        View.SizeChanged += (_, _) => FitAgentOutput();
    }

    private Size? _agentOutput;
    private int _fits;

    private void SetFixedSize(double width, double height)
    {
        MinWidth = 0;
        MinHeight = 0;
        MaxWidth = double.PositiveInfinity;
        MaxHeight = double.PositiveInfinity;
        Width = width;
        Height = height;
        MinWidth = MaxWidth = width;
        MinHeight = MaxHeight = height;
    }

    private void FitAgentOutput()
    {
        if (_agentOutput is not { } wanted || _fits >= 4)
        {
            return;
        }

        var bounds = View.Bounds.Size;
        var dx = wanted.Width - bounds.Width;
        var dy = wanted.Height - bounds.Height;
        if (Math.Abs(dx) < 0.5 && Math.Abs(dy) < 0.5)
        {
            return;
        }

        _fits++;
        SetFixedSize(Width + dx, Height + dy);
    }

    public void SetAgentStatus(string status, bool paused)
    {
        ArgumentNullException.ThrowIfNull(status);
        if (_agentStatus is not null)
        {
            _agentStatus.Text = status;
        }

        if (_resume is not null)
        {
            _resume.IsVisible = paused;
        }
    }

    public void SetTitle(string title)
    {
        _title = title;
        Title = title;
    }

    public void Present()
    {
        if (!IsVisible)
        {
            Show();
        }

        Activate();
    }

    public void AllowClose() => _allowClose = true;

    public async Task CloseAsync()
    {
        if (IsVisible)
        {
            StateChanged?.Invoke(CurrentState());
        }

        if (View.Toplevel is { } toplevel)
        {
            await toplevel.ShutdownAsync();
        }

        AllowClose();
        Close();
    }

    public ShellWindowState CurrentState()
    {
        var fullScreen = WindowState == WindowState.FullScreen;
        if (fullScreen || WindowState == WindowState.Maximized || WindowState == WindowState.Minimized)
        {
            return _lastNormal with { FullScreen = fullScreen };
        }

        _lastNormal = new ShellWindowState(
            Math.Max(320, (int)Math.Round(Width)),
            Math.Max(240, (int)Math.Round(Height)),
            Position.X,
            Position.Y,
            false);
        return _lastNormal;
    }

    private bool FitsAScreen()
    {
        if (_initial.X is null || _initial.Y is null)
        {
            return true;
        }

        var scaling = RenderScaling > 0 ? RenderScaling : 1.0;
        var rect = new PixelRect(Position, new PixelSize((int)(Width * scaling), (int)(Height * scaling)));
        foreach (var screen in Screens.All)
        {
            if (screen.Bounds.Intersects(rect))
            {
                return true;
            }
        }

        return false;
    }

    private void ScheduleSave()
    {
        if (_saveTimer is null)
        {
            _saveTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(750), DispatcherPriority.Background, (_, _) =>
            {
                _saveTimer!.Stop();
                StateChanged?.Invoke(CurrentState());
            });
        }

        _saveTimer.Stop();
        _saveTimer.Start();
    }
}
