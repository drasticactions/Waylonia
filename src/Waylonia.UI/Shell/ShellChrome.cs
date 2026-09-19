using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basin;
using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Scene;
using Basin.Shell.Nested;
using Basin.UI.Avalonia;
using Waylonia.UI;

namespace Waylonia.Shell;

internal sealed class ShellChrome : IDisposable
{
    private readonly NestedShell _shell;
    private readonly IShellHost _host;
    private readonly Interactive _pressSource;
    private readonly PanelModel _model;
    private PanelArrangement _arrangement;
    private readonly Action<Action> _post;
    private readonly BasinLogger _log;
    private readonly AvaloniaUIHost _ui;
    private readonly ShellScreenSource _screens;
    private readonly List<PanelStrip> _panels = [];
    private readonly Dictionary<IUISurface, UISurfaceNode> _popupNodes = [];
    private readonly HashSet<IUISurface> _openPopups = [];
    private ChromeSlot? _manager;
    private ChromeSlot? _settings;
    private AvaloniaUISurface? _switcherSurface;
    private UISurfaceNode? _switcherNode;
    private SwitcherView? _switcherView;
    private IReadOnlyList<SwitcherEntry> _switcherEntries = [];
    private bool _disposed;
    private bool _reportedFrame;

    private sealed record PanelStrip(bool Top, AvaloniaUISurface Surface, PanelView View, UISurfaceNode? Node)
    {
        public UISurfaceNode? Node { get; set; } = Node;
    }

    private sealed class ChromeSlot
    {
        public required AvaloniaUISurface Surface { get; init; }

        public required ChromeContent Content { get; init; }

        public ManagedWindow? Window { get; set; }

        public bool Closing { get; set; }
    }

    public ShellChrome(NestedShell shell, IShellHost host, PanelModel model, PanelArrangement panels, Action<Action> post, BasinLogger log)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(panels);
        ArgumentNullException.ThrowIfNull(post);
        _shell = shell;
        _host = host;
        _pressSource = host.TopLevel ?? (Interactive)host.View;
        _model = model;
        _arrangement = panels;
        _post = post;
        _log = log;
        _screens = new ShellScreenSource(shell.Output, shell.Scale);
        _ui = BasinPlatform.Attach(new BasinPlatformOptions
        {
            Screens = _screens,
            CompositorAffinity = shell.Host.Affinity,
            Features = type => type == typeof(IClipboard) ? host.TopLevel?.Clipboard : null,
        });
        _ui.SurfaceDamaged += OnSurfaceDamaged;
        _ui.PopupAppeared += OnPopupAppeared;
        _ui.PopupDismissed += OnPopupDismissed;
        _shell.PopupDismissRequested += OnPopupDismissRequested;
        _host.DeactivatedOnHost += DismissPopups;
        _pressSource.AddHandler(InputElement.PointerPressedEvent, OnWindowPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        BuildPanels();
    }

    public PanelModel Model => _model;

    private void BuildPanels()
    {
        foreach (var strip in _panels)
        {
            DestroyPanel(strip);
        }

        _panels.Clear();
        var layout = _arrangement;
        if (layout.Top.Count > 0)
        {
            _panels.Add(CreatePanel(top: true, layout.Top));
        }

        if (layout.Bottom.Count > 0)
        {
            _panels.Add(CreatePanel(top: false, layout.Bottom));
        }
    }

    private PanelStrip CreatePanel(bool top, IReadOnlyList<PanelApplet> applets)
    {
        var strip = top ? _shell.TopStrip : _shell.BottomStrip;
        var surface = (AvaloniaUISurface)(_ui.CreateSurface(new UISurfaceOptions
        {
            Target = UITargetKind.Memory,
            Width = Math.Max(1, strip.Width),
            Height = Math.Max(1, strip.Height),
            Scale = _shell.Scale,
        }) ?? throw new InvalidOperationException("the attached Avalonia host declined to create a panel surface"));
        var view = new PanelView { DataContext = new PanelViewModel(_model, applets) };
        surface.Content = view;
        if (surface.Root is { } root)
        {
            root.RequestedThemeVariant = _host.View.ActualThemeVariant;
        }

        surface.SetPosition(strip.X, strip.Y);
        var panel = new PanelStrip(top, surface, view, null);
        var layer = _shell.Layers.Panel;
        var index = _shell.UISurfaces;
        _post(() =>
        {
            var node = new UISurfaceNode(layer, surface, index) { PreciseDamage = true };
            node.SetPosition(strip.X, strip.Y);
            panel.Node = node;
            surface.PublishDamage();
        });
        return panel;
    }

    private void DestroyPanel(PanelStrip strip)
    {
        var node = strip.Node;
        _post(() => node?.Dispose());
        strip.Surface.Dispose();
    }

    public void Relayout()
    {
        if (_disposed)
        {
            return;
        }

        _screens.Update(_shell.Output, _shell.Scale);
        foreach (var panel in _panels)
        {
            var strip = panel.Top ? _shell.TopStrip : _shell.BottomStrip;
            panel.Surface.Configure(Math.Max(1, strip.Width), Math.Max(1, strip.Height), _shell.Scale);
            panel.Surface.SetPosition(strip.X, strip.Y);
            var node = panel.Node;
            _post(() => node?.SetPosition(strip.X, strip.Y));
        }

        if (_switcherSurface is not null)
        {
            PlaceSwitcher();
        }
    }

    public void RebuildPanels(PanelArrangement panels)
    {
        ArgumentNullException.ThrowIfNull(panels);
        if (_disposed)
        {
            return;
        }

        _arrangement = panels;
        BuildPanels();
    }

    public void ApplyThemeVariant()
    {
        foreach (var panel in _panels)
        {
            if (panel.Surface.Root is { } root)
            {
                root.RequestedThemeVariant = _host.View.ActualThemeVariant;
            }
        }
    }

    public void OpenManager(ManagerViewModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        OpenChrome(ref _manager, "Sessions", "waylonia.sessions", ManagerView.DefaultWidth, ManagerView.DefaultHeight,
            () => new ManagerView { DataContext = model }, slot => _manager = slot);
    }

    public void OpenSettings(SettingsViewModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        OpenChrome(ref _settings, "Settings", "waylonia.settings", SettingsView.DefaultWidth, SettingsView.DefaultHeight,
            () => new SettingsView { DataContext = model }, slot => _settings = slot);
    }

    public Task<string?> AskPass(string prompt, AskPassKind kind)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        var answered = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (_disposed)
        {
            answered.TrySetResult(null);
            return answered.Task;
        }

        var view = new AskPassView(prompt, kind);
        ChromeSlot? slot = null;
        view.Answered += answer =>
        {
            answered.TrySetResult(answer);
            if (slot is { } open)
            {
                CloseChrome(open, _ => { });
            }
        };
        OpenChrome(ref slot, "Waylonia", "waylonia.askpass", AskPassView.DefaultWidth, AskPassView.DefaultHeight,
            () => view, created =>
            {
                if (created is null)
                {
                    answered.TrySetResult(null);
                }
            }, centered: true, minWidth: 320, minHeight: 120);
        return answered.Task;
    }

    private void OpenChrome(
        ref ChromeSlot? slot, string title, string appId, int width, int height, Func<Control> view, Action<ChromeSlot?> store,
        bool centered = false, int minWidth = 320, int minHeight = 240)
    {
        if (_disposed)
        {
            return;
        }

        if (slot is { Closing: false } open)
        {
            var window = open.Window;
            _post(() =>
            {
                if (window is { IsMapped: true })
                {
                    _shell.ActivateWindow(window);
                }
            });
            return;
        }

        var area = _shell.WorkArea;
        width = Math.Max(minWidth, Math.Min(width, area.Width - 40));
        height = Math.Max(minHeight, Math.Min(height, area.Height - 40));
        var surface = (AvaloniaUISurface?)_ui.CreateSurface(new UISurfaceOptions
        {
            Target = UITargetKind.Memory,
            Width = width,
            Height = height,
            Scale = _shell.Scale,
        });
        if (surface is null)
        {
            _log.Error($"the attached Avalonia host declined to create the {title} window");
            return;
        }

        surface.Content = view();
        if (surface.Root is { } root)
        {
            root.RequestedThemeVariant = _host.View.ActualThemeVariant;
        }

        ChromeSlot? created = null;
        var content = new ChromeContent(
            surface,
            title,
            appId,
            _shell.UISurfaces,
            _shell.Scale,
            (w, h, scale) => Dispatcher.UIThread.Post(() =>
            {
                if (!_disposed && created is { Closing: false })
                {
                    surface.Configure(w, h, scale);
                }
            }),
            (x, y) => Dispatcher.UIThread.Post(() =>
            {
                if (!_disposed && created is { Closing: false })
                {
                    surface.SetPosition(x, y);
                }
            }),
            () => Dispatcher.UIThread.Post(() =>
            {
                if (created is not null)
                {
                    CloseChrome(created, store);
                }
            }))
        {
            Centered = centered,
            MinWidth = minWidth,
            MinHeight = minHeight,
        };
        created = new ChromeSlot { Surface = surface, Content = content };
        slot = created;
        store(created);
        var opened = created;
        _post(() => opened.Window = _shell.Adopt(content));
    }

    private void CloseChrome(ChromeSlot slot, Action<ChromeSlot?> store)
    {
        if (slot.Closing)
        {
            return;
        }

        slot.Closing = true;
        store(null);
        var surface = slot.Surface;
        _post(() =>
        {
            if (slot.Window is { } window)
            {
                _shell.Release(window);
            }

            Dispatcher.UIThread.Post(surface.Dispose);
        });
    }

    public void OpenMainMenu()
    {
        foreach (var panel in _panels)
        {
            if (!panel.Top)
            {
                continue;
            }

            foreach (var menu in panel.View.GetVisualDescendants().OfType<Menu>())
            {
                if (menu.Items.Count > 0 && menu.Items[0] is MenuItem first)
                {
                    menu.Focus();
                    first.Open();
                    return;
                }
            }
        }
    }

    public void ShowSwitcher(IReadOnlyList<ManagedWindow> order, int index)
    {
        if (_disposed)
        {
            return;
        }

        var entries = new List<SwitcherEntry>(order.Count);
        foreach (var window in order)
        {
            entries.Add(new SwitcherEntry(window.Title, window.Suffix, window.IconName is { } icon && Path.IsPathRooted(icon) ? icon : null));
        }

        _switcherEntries = entries;
        if (index >= 0 && index < entries.Count)
        {
            entries[index].IsSelected = true;
        }

        var height = SwitcherView.HeightFor(entries.Count);
        if (_switcherSurface is null)
        {
            _switcherSurface = (AvaloniaUISurface?)_ui.CreateSurface(new UISurfaceOptions
            {
                Target = UITargetKind.Memory,
                Width = SwitcherView.PanelWidth,
                Height = height,
                Scale = _shell.Scale,
            });
            if (_switcherSurface is null)
            {
                return;
            }

            _switcherView = new SwitcherView();
            _switcherSurface.Content = _switcherView;
            var surface = _switcherSurface;
            var layer = _shell.Layers.Switcher;
            var indexOf = _shell.UISurfaces;
            _post(() =>
            {
                _switcherNode = new UISurfaceNode(layer, surface, indexOf) { PreciseDamage = true, InputEnabled = false };
                PlaceSwitcherNode();
                surface.PublishDamage();
            });
        }
        else
        {
            _switcherSurface.Configure(SwitcherView.PanelWidth, height, _shell.Scale);
        }

        _switcherView!.Show(entries);
        PlaceSwitcher();
    }

    public void MoveSwitcher(int index)
    {
        for (var i = 0; i < _switcherEntries.Count; i++)
        {
            _switcherEntries[i].IsSelected = i == index;
        }
    }

    public void HideSwitcher()
    {
        if (_switcherSurface is not { } surface)
        {
            return;
        }

        _switcherSurface = null;
        _switcherView = null;
        _switcherEntries = [];
        _post(() =>
        {
            _switcherNode?.Dispose();
            _switcherNode = null;
        });
        surface.Dispose();
    }

    private void PlaceSwitcher()
    {
        if (_switcherSurface is not { } surface)
        {
            return;
        }

        var size = surface.Size;
        var area = _shell.WorkArea;
        var x = area.X + ((area.Width - size.Width) / 2);
        var y = area.Y + ((area.Height - size.Height) / 2);
        surface.SetPosition(x, y);
        _post(PlaceSwitcherNode);
    }

    private void PlaceSwitcherNode()
    {
        if (_switcherNode is { } node && _switcherSurface is { } surface)
        {
            node.SetPosition((int)surface.PositionX, (int)surface.PositionY);
        }
    }

    private void OnSurfaceDamaged(IUISurface surface)
    {
        if (_disposed || surface is not AvaloniaUISurface attached)
        {
            return;
        }

        _post(() =>
        {
            if (_popupNodes.TryGetValue(attached, out var node))
            {
                node.SetPosition((int)Math.Round(attached.PositionX), (int)Math.Round(attached.PositionY));
            }

            if (attached.PublishDamage() && !_reportedFrame)
            {
                _reportedFrame = true;
                _log.Debug($"the first panel frame reached the scene");
            }
        });
    }

    private void OnPopupAppeared(IUISurface popup)
    {
        if (_disposed || popup is not AvaloniaUISurface attached)
        {
            return;
        }

        _openPopups.Add(popup);
        var layer = _shell.Layers.Menu;
        var index = _shell.UISurfaces;
        var owner = attached.Owner;
        var fromPanel = IsPanel(owner);
        var panelPopupsBefore = _panelPopups;
        if (fromPanel)
        {
            _panelPopups++;
        }

        _post(() =>
        {
            var node = new UISurfaceNode(layer, attached, index) { PreciseDamage = true };
            node.SetPosition((int)Math.Round(attached.PositionX), (int)Math.Round(attached.PositionY));
            node.Node.RaiseToTop();
            _popupNodes[attached] = node;
            _shell.PopupOpened(attached, owner);
            attached.PublishDamage();
            if (fromPanel && panelPopupsBefore == 0)
            {
                _shell.GivePanelKeyboard(owner);
            }
        });
    }

    private int _panelPopups;

    private bool IsPanel(AvaloniaUISurface surface)
    {
        foreach (var panel in _panels)
        {
            if (ReferenceEquals(panel.Surface, surface))
            {
                return true;
            }
        }

        return false;
    }

    private void OnPopupDismissed(IUISurface popup)
    {
        if (!_openPopups.Remove(popup) || popup is not AvaloniaUISurface attached)
        {
            return;
        }

        var release = false;
        if (IsPanel(attached.Owner) && _panelPopups > 0 && --_panelPopups == 0)
        {
            release = true;
        }

        _post(() =>
        {
            _shell.PopupClosed(popup);
            if (_popupNodes.Remove(popup, out var node))
            {
                node.Dispose();
            }
        });
        if (release)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (!_disposed && _panelPopups == 0)
                {
                    _post(_shell.ReleasePanelKeyboard);
                }
            }, DispatcherPriority.Background);
        }
    }

    private void OnPopupDismissRequested() => Dispatcher.UIThread.Post(DismissPopups);

    private void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_host.View.Toplevel is { } view && e.Source is global::Avalonia.Visual pressed
            && !ReferenceEquals(pressed, view) && !view.IsVisualAncestorOf(pressed))
        {
            DismissPopups();
        }
    }

    private void DismissPopups()
    {
        if (!_disposed && _openPopups.OfType<AvaloniaUISurface>().FirstOrDefault() is { } popup)
        {
            popup.Owner.NotifyPressOutside();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _shell.PopupDismissRequested -= OnPopupDismissRequested;
        _host.DeactivatedOnHost -= DismissPopups;
        _pressSource.RemoveHandler(InputElement.PointerPressedEvent, OnWindowPointerPressed);
        _ui.SurfaceDamaged -= OnSurfaceDamaged;
        _ui.PopupAppeared -= OnPopupAppeared;
        _ui.PopupDismissed -= OnPopupDismissed;
        HideSwitcher();
        if (_manager is { } manager)
        {
            CloseChrome(manager, slot => _manager = slot);
        }

        if (_settings is { } settings)
        {
            CloseChrome(settings, slot => _settings = slot);
        }

        foreach (var strip in _panels)
        {
            DestroyPanel(strip);
        }

        _panels.Clear();
        _ui.Dispose();
    }
}
