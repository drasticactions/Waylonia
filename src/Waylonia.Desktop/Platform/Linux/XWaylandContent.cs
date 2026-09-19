using Basin;
using Basin.Capabilities;
using Basin.Scene;
using Basin.Shell.Xdg;
using Basin.XWayland;
using Waylonia.Shell;

namespace Waylonia;

internal sealed class XWaylandContent : IWindowContent
{
    private readonly Func<XWaylandWindow, IWindowContent?> _resolveParent;
    private readonly XWaylandToplevelSource? _toplevels;
    private SceneSurface? _scene;
    private bool _maximized;
    private bool _fullscreen;
    private bool _resizing;

    public XWaylandContent(
        XWaylandWindow window,
        Surface surface,
        Func<XWaylandWindow, IWindowContent?> resolveParent,
        XWaylandToplevelSource? toplevels)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(surface);
        Window = window;
        Surface = surface;
        _resolveParent = resolveParent;
        _toplevels = toplevels;
        window.TitleChanged += () => TitleChanged?.Invoke();
        window.PropertiesChanged += () =>
        {
            AppIdChanged?.Invoke();
            ParentChanged?.Invoke();
        };
        window.DecorationsChanged += () => DecorationsChanged?.Invoke();
        surface.Committed += () => Committed?.Invoke();
    }

    public XWaylandWindow Window { get; }

    public string Title => Window.Title;

    public string AppId => Window.Class is { Length: > 0 } wmClass ? wmClass : Window.Instance;

    public Box Geometry
    {
        get
        {
            var current = Surface.Current;
            return current.Buffer is null
                ? new Box(0, 0, Math.Max(1, Window.Width), Math.Max(1, Window.Height))
                : new Box(0, 0, Math.Max(1, current.Width), Math.Max(1, current.Height));
        }
    }

    public int MinWidth => Window.MinWidth;

    public int MinHeight => Window.MinHeight;

    public int MaxWidth => Window.MaxWidth;

    public int MaxHeight => Window.MaxHeight;

    public IWindowContent? Parent => Window.TransientFor is { } parent ? _resolveParent(parent) : null;

    public Surface Surface { get; }

    public IUISurface? UISurface => null;

    public bool Maximized => _maximized;

    public bool Fullscreen => _fullscreen;

    public bool Resizing => _resizing;

    public bool ServerDecorated => Window.WantsDecorations;

    public bool Centered => false;

    public FrameCapabilities Capabilities =>
        FrameCapabilities.WindowMenu | FrameCapabilities.Maximize | FrameCapabilities.Minimize
        | FrameCapabilities.Fullscreen | FrameCapabilities.Shade | FrameCapabilities.Above | FrameCapabilities.Stick;

    public event Action? TitleChanged;

    public event Action? AppIdChanged;

    public event Action? ParentChanged;

    public event Action? DecorationsChanged;

    public event Action? Committed;

    public SceneNode Attach(SceneTree tree)
    {
        _scene = new SceneSurface(tree, Surface);
        return _scene.Tree;
    }

    public void Detach()
    {
        if (_scene is { IsDestroyed: false } scene)
        {
            scene.Destroy();
        }

        _scene = null;
    }

    public void SetActivated(bool activated)
    {
        if (activated)
        {
            Window.Activate();
        }
    }

    public void SetMaximized(bool maximized)
    {
        _maximized = maximized;
        Window.SetMaximized(maximized);
    }

    public void SetFullscreen(bool fullscreen)
    {
        _fullscreen = fullscreen;
        Window.SetFullscreen(fullscreen);
    }

    public void SetResizing(bool resizing) => _resizing = resizing;

    public void SetMinimized(bool minimized) => Window.SetMinimized(minimized);

    public void Raise() => Window.Raise();

    public void Lower() => Window.Lower();

    public void SetTiled(ResizeEdges edges)
    {
    }

    public void SetSize(int width, int height)
    {
        if (width <= 0 || height <= 0 || (width == Window.Width && height == Window.Height))
        {
            return;
        }

        Window.Configure(Window.X, Window.Y, width, height);
    }

    public void SetBounds(int width, int height)
    {
    }

    public void SetPosition(int x, int y)
    {
        if (x == Window.X && y == Window.Y)
        {
            return;
        }

        Window.Configure(x, y, Window.Width, Window.Height);
    }

    public void OutputScaleChanged(double scale)
    {
    }

    public void ReportGeometry(in Box frame, in Box client) => _toplevels?.SetGeometry(Window, frame, client);

    public void Close() => Window.Close();

    public bool Owns(Surface surface)
    {
        for (var candidate = surface; candidate is not null;)
        {
            if (candidate == Surface)
            {
                return true;
            }

            candidate = candidate.SubsurfaceRole?.Parent;
        }

        return false;
    }
}
