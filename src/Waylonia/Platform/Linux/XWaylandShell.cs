using Basin;
using Basin.Diagnostics;
using Basin.XWayland;
using Waylonia.Shell;

namespace Waylonia;

internal sealed class XWaylandShell
{
    private const string IconSession = "x11";

    private readonly XWaylandModule _module;
    private readonly NestedShell _shell;
    private readonly IconCache _icons;
    private readonly BasinLogger _log;
    private readonly Dictionary<XWaylandWindow, Managed> _managed = [];

    public XWaylandShell(XWaylandModule module, NestedShell shell, IconCache icons, BasinLogger log)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(icons);
        _module = module;
        _shell = shell;
        _icons = icons;
        _log = log;
        if (module.WindowManager is { } wm)
        {
            Attach(wm);
        }

        module.WindowManagerReady += Attach;
    }

    private void Attach(XWaylandWm wm)
    {
        wm.WindowMapped += OnMapped;
        wm.OverrideRedirectMapped += OnOverrideRedirectMapped;
        wm.ActivationRequested += OnActivationRequested;
        wm.ConfigureRequest = OnConfigureRequest;
    }

    private bool OnConfigureRequest(XWaylandWindow xwindow, Box requested)
    {
        if (!_managed.TryGetValue(xwindow, out var managed) || !managed.Window.IsMapped)
        {
            return true;
        }

        var window = managed.Window;
        if (window.Maximized || window.Fullscreen || window.Tile != TileEdge.None)
        {
            xwindow.Configure(xwindow.X, xwindow.Y, xwindow.Width, xwindow.Height);
            return false;
        }

        var client = window.ClientBox;
        var (width, height) = Constraints.ClampSize(
            Math.Max(1, requested.Width), Math.Max(1, requested.Height),
            window.Content.MinWidth, window.Content.MinHeight, window.Content.MaxWidth, window.Content.MaxHeight);
        var target = requested.X != client.X || requested.Y != client.Y
            ? KeepOnScreen(window, requested.X, requested.Y, width, height)
            : new Point(client.X, client.Y);
        xwindow.Configure(target.X, target.Y, width, height);
        if (target.X != client.X || target.Y != client.Y)
        {
            window.MoveClientTo(target.X, target.Y);
        }

        window.RefreshFrame();
        _shell.Publish();
        return false;
    }

    private Point KeepOnScreen(ManagedWindow window, int x, int y, int width, int height)
    {
        var insets = window.Insets;
        var wanted = new Box(
            x - insets.Left, y - insets.Top, width + insets.Left + insets.Right, height + insets.Top + insets.Bottom);
        var kept = Constraints.KeepTitleOnScreen(wanted, _shell.WorkArea, window.TitleHeight);
        return new Point(kept.X + insets.Left, kept.Y + insets.Top);
    }

    private void OnMapped(XWaylandWindow xwindow)
    {
        if (xwindow.Surface is not { IsDestroyed: false } surface || _managed.ContainsKey(xwindow))
        {
            return;
        }

        var content = new XWaylandContent(
            xwindow, surface, parent => _managed.GetValueOrDefault(parent)?.Window.Content, _module.Toplevels);
        var window = _shell.Adopt(content, StoreIcon(xwindow));
        var managed = new Managed(window);
        _managed[xwindow] = managed;
        if (xwindow.WantsFullscreen)
        {
            _shell.SetFullscreen(window, true);
        }
        else if (xwindow.WantsMaximized)
        {
            _shell.SetMaximized(window, true);
        }

        managed.Release = () =>
        {
            if (!_managed.Remove(xwindow))
            {
                return;
            }

            _log.Debug($"x11 window 0x{xwindow.WindowId:x} left the shell");
            xwindow.Unmapped -= managed.Release;
            xwindow.Destroyed -= managed.Release;
            surface.Destroyed -= managed.Release;
            xwindow.MinimizeRequested -= managed.Minimize;
            xwindow.MaximizeRequested -= managed.Maximize;
            xwindow.FullscreenRequested -= managed.Fullscreen;
            xwindow.GeometryChanged -= managed.Follow;
            xwindow.IconChanged -= managed.Icon;
            _shell.Release(window);
        };
        managed.Minimize = minimized =>
        {
            if (window.IsMapped)
            {
                _shell.SetMinimized(window, minimized);
            }
        };
        managed.Maximize = maximized =>
        {
            if (window.IsMapped && maximized != window.Maximized)
            {
                _shell.SetMaximized(window, maximized);
            }
        };
        managed.Fullscreen = fullscreen =>
        {
            if (window.IsMapped)
            {
                _shell.SetFullscreen(window, fullscreen);
            }
        };
        managed.Follow = () => FollowClientMove(window, xwindow);
        managed.Icon = () => _shell.SetClientIcon(window, StoreIcon(xwindow));
        xwindow.Unmapped += managed.Release;
        xwindow.Destroyed += managed.Release;
        surface.Destroyed += managed.Release;
        xwindow.MinimizeRequested += managed.Minimize;
        xwindow.MaximizeRequested += managed.Maximize;
        xwindow.FullscreenRequested += managed.Fullscreen;
        xwindow.GeometryChanged += managed.Follow;
        xwindow.IconChanged += managed.Icon;
        _log.Debug($"x11 window 0x{xwindow.WindowId:x} '{xwindow.Title}' ({xwindow.Class}) joined the shell");
    }

    private void FollowClientMove(ManagedWindow window, XWaylandWindow xwindow)
    {
        if (!window.IsMapped || window.Maximized || window.Fullscreen || window.Tile != TileEdge.None)
        {
            return;
        }

        var client = window.ClientBox;
        if (client.X == xwindow.X && client.Y == xwindow.Y)
        {
            return;
        }

        var target = KeepOnScreen(window, xwindow.X, xwindow.Y, client.Width, client.Height);
        window.MoveClientTo(target.X, target.Y);
        window.RefreshFrame();
        _shell.Publish();
    }

    private void OnOverrideRedirectMapped(XWaylandWindow xwindow)
    {
        if (xwindow.Surface is not { IsDestroyed: false } surface)
        {
            return;
        }

        var scene = _shell.AddUnmanaged(surface, xwindow.X, xwindow.Y);
        var unmanaged = new Unmanaged();
        _log.Debug($"x11 override-redirect window 0x{xwindow.WindowId:x} shown at {xwindow.X},{xwindow.Y} {xwindow.Width}x{xwindow.Height}");
        unmanaged.Layout = () =>
        {
            if (!scene.IsDestroyed)
            {
                scene.Tree.SetPosition(xwindow.X, xwindow.Y);
            }
        };
        unmanaged.Drop = () =>
        {
            _log.Debug($"x11 override-redirect window 0x{xwindow.WindowId:x} gone");
            xwindow.GeometryChanged -= unmanaged.Layout;
            xwindow.Unmapped -= unmanaged.Drop;
            xwindow.Destroyed -= unmanaged.Drop;
            if (!scene.IsDestroyed)
            {
                scene.Destroy();
            }
        };
        xwindow.GeometryChanged += unmanaged.Layout;
        xwindow.Unmapped += unmanaged.Drop;
        xwindow.Destroyed += unmanaged.Drop;
    }

    private void OnActivationRequested(XWaylandWindow xwindow)
    {
        if (_managed.TryGetValue(xwindow, out var managed) && managed.Window.IsMapped)
        {
            _shell.ActivateWindow(managed.Window);
        }
    }

    private string? StoreIcon(XWaylandWindow xwindow)
    {
        if (xwindow.Icon is not { } icon)
        {
            return null;
        }

        var name = xwindow.Class is { Length: > 0 } wmClass ? wmClass : xwindow.Instance is { Length: > 0 } instance ? instance : "window";
        var stem = ArgbIcon.Stem(name, icon.Pixels);
        if (_icons.Find(IconSession, stem) is { } cached)
        {
            return cached;
        }

        if (ArgbIcon.Png(icon.Width, icon.Height, icon.Pixels) is not { } png)
        {
            return null;
        }

        return _icons.Store(IconSession, new RemoteIcon(stem, "png", png), _log);
    }

    private sealed class Managed(ManagedWindow window)
    {
        public ManagedWindow Window { get; } = window;

        public Action Release { get; set; } = () => { };

        public Action<bool> Minimize { get; set; } = _ => { };

        public Action<bool> Maximize { get; set; } = _ => { };

        public Action<bool> Fullscreen { get; set; } = _ => { };

        public Action Follow { get; set; } = () => { };

        public Action Icon { get; set; } = () => { };
    }

    private sealed class Unmanaged
    {
        public Action Layout { get; set; } = () => { };

        public Action Drop { get; set; } = () => { };
    }
}
