using Basin;
using Basin.Capabilities;
using Basin.Scene;
using Basin.Shell.Nested;
using Basin.Shell.Xdg;
using Basin.UI.Avalonia;
using Pixman;

namespace Waylonia.Shell;

internal sealed class ChromeContent : IWindowContent, IUISurfaceObserver
{
    private readonly UISurfaceIndex _index;
    private readonly Action<int, int, double> _configure;
    private readonly Action<int, int> _move;
    private readonly Action _close;
    private UISurfaceNode? _node;
    private double _scale;
    private int _width;
    private int _height;

    public ChromeContent(
        AvaloniaUISurface surface,
        string title,
        string appId,
        UISurfaceIndex index,
        double scale,
        Action<int, int, double> configure,
        Action<int, int> move,
        Action close)
    {
        ArgumentNullException.ThrowIfNull(surface);
        Surface = surface;
        Title = title;
        AppId = appId;
        _index = index;
        _scale = scale;
        _configure = configure;
        _move = move;
        _close = close;
        _width = surface.Size.Width;
        _height = surface.Size.Height;
    }

    public AvaloniaUISurface Surface { get; }

    public bool Centered { get; init; }

    public string Title { get; }

    public string AppId { get; }

    public Box Geometry => new(0, 0, Math.Max(1, _width), Math.Max(1, _height));

    public int MinWidth { get; init; } = 320;

    public int MinHeight { get; init; } = 240;

    public int MaxWidth => 0;

    public int MaxHeight => 0;

    public IWindowContent? Parent => null;

    Surface? IWindowContent.Surface => null;

    public IUISurface? UISurface => Surface;

    public bool Maximized { get; private set; }

    public bool Fullscreen { get; private set; }

    public bool Resizing { get; private set; }

    public bool ServerDecorated => true;

    public FrameCapabilities Capabilities =>
        FrameCapabilities.WindowMenu | FrameCapabilities.Maximize | FrameCapabilities.Minimize
        | FrameCapabilities.Shade | FrameCapabilities.Above | FrameCapabilities.Stick;

    public event Action? TitleChanged
    {
        add
        {
        }

        remove
        {
        }
    }

    public event Action? AppIdChanged
    {
        add
        {
        }

        remove
        {
        }
    }

    public event Action? ParentChanged
    {
        add
        {
        }

        remove
        {
        }
    }

    public event Action? DecorationsChanged
    {
        add
        {
        }

        remove
        {
        }
    }

    public event Action? Committed;

    public SceneNode Attach(SceneTree tree)
    {
        _node = new UISurfaceNode(tree, Surface, _index) { PreciseDamage = true };
        Surface.AddObserver(this);
        Surface.PublishDamage();
        return _node.Node;
    }

    public void Detach()
    {
        Surface.RemoveObserver(this);
        _node?.Dispose();
        _node = null;
    }

    public void OnSurfaceDamaged(IUISurface surface, PixmanRegion32 damage)
    {
        var size = Surface.Size;
        var changed = size.Width != _width || size.Height != _height;
        _width = size.Width;
        _height = size.Height;
        if (changed)
        {
            Committed?.Invoke();
        }
    }

    public void OnSurfaceDestroyed(IUISurface surface)
    {
    }

    public void SetActivated(bool activated)
    {
    }

    public void SetMaximized(bool maximized) => Maximized = maximized;

    public void SetFullscreen(bool fullscreen) => Fullscreen = fullscreen;

    public void SetResizing(bool resizing) => Resizing = resizing;

    public void SetMinimized(bool minimized)
    {
    }

    public void Raise()
    {
    }

    public void Lower()
    {
    }

    public void SetTiled(ResizeEdges edges)
    {
    }

    public void SetSize(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        _configure(width, height, _scale);
    }

    public void SetBounds(int width, int height)
    {
    }

    public void SetPosition(int x, int y) => _move(x, y);

    public void OutputScaleChanged(double scale)
    {
        _scale = scale;
        _configure(Math.Max(1, _width), Math.Max(1, _height), scale);
    }

    public void ReportGeometry(in Box frame, in Box client)
    {
    }

    public void Close() => _close();

    public bool Owns(Surface surface) => false;
}
