using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Basin;
using Basin.Avalonia;
using Basin.Diagnostics;
using Basin.Freedesktop;
using Basin.Scene;
using Wayland.Server;
using Waylonia.Audio;
using Waylonia.Cli;
using Waylonia.Sessions;
using Waylonia.Ui;
using static Waylonia.WayloniaLog;

namespace Waylonia;

internal sealed class WayloniaApp : Application, ISessionHost
{
    private static WayloniaRun? _run;
    private static ConfigValues? _startup;
    private static int _exitStatus;
    private static long _rendered;

    private readonly ChannelClients _channelClients = new();
    private readonly List<System.Runtime.InteropServices.PosixSignalRegistration> _signals = [];
    private readonly AudioMixer _mixer = new();
    private BasinOutputView? _view;
    private BasinCompositorHost? _host;
    private ToplevelWindows? _windows;
    private HostClipboard? _clipboard;
    private HostDrag? _hostDrag;
    private AvaloniaTextInput? _textInput;
    private Process? _localClient;
    private Window? _window;
    private TrayIcon? _tray;
    private TrayMenu? _trayMenu;
    private IDisposable? _globalHotkeys;
    private string _hotkeySignature = string.Empty;
    private bool _hotkeysDisarmed;
    private DesktopShellPolicy? _desktop;
    private CaptureToggle? _capture;
    private Basin.IProtocolModule? _xwayland;
    private WireClockClients? _wireClock;
    private DispatcherTimer? _channelPump;
    private int _attachedClients;
    private WaypipeAcceptor? _listen;
    private SessionRegistry? _registry;
    private SessionCatalog _catalog = SessionCatalog.Empty;
    private ManagerWindow? _manager;
    private SettingsWindow? _settings;
    private readonly Dictionary<ToplevelWindow, string> _sessionWindows = [];
    private ToplevelWindow? _screenWindow;
    private string _screenTitle = string.Empty;
    private IReadOnlyList<ApplicationMenuItem>? _localApplications;
    private string? _localNotice;
    private bool _shuttingDown;

    public static long Rendered => Interlocked.Read(ref _rendered);

    public override void Initialize() => Styles.Add(new global::BluerCurve.BluerCurveTheme());

    public static int Run(WayloniaRun run)
    {
        _run = run;
        _startup = run.Config.Values;
        _exitStatus = 0;
        var builder = AppBuilder.Configure<WayloniaApp>().UsePlatformDetect().UseHostWindowing()
            .With(new MacOSPlatformOptions { ShowInDock = false });
        var status = builder.StartWithClassicDesktopLifetime([]);
        return status != 0 ? status : _exitStatus;
    }

    BasinCompositorHost ISessionHost.Compositor => _host!;

    HostSettings ISessionHost.Settings => _run!.Host;

    AudioMixer ISessionHost.Audio => _mixer;

    bool ISessionHost.ShuttingDown => _shuttingDown;

    void ISessionHost.Post(Action action) => _view!.Post(action);

    void ISessionHost.Status(string text) => UpdateStatus(text);

    void ISessionHost.Attach(WaypipeAcceptor owner, WlClient client)
    {
        _channelClients.Add(client, owner);
        _wireClock?.Add(client);
        if (owner.Session is { IsDesktop: true } && _desktop is { HasClaimed: false } desktop)
        {
            desktop.Declare(client);
        }

        Dispatcher.UIThread.Post(() =>
        {
            _attachedClients++;
            if (_channelPump is null && !_shuttingDown)
            {
                _channelPump = new DispatcherTimer(
                    TimeSpan.FromMilliseconds(16), DispatcherPriority.Background, (_, _) => _view?.RequestFrame());
                _channelPump.Start();
            }
        });
        Protocol($"CHANNEL {owner.Name} {owner.Attached} attached");
    }

    public override void OnFrameworkInitializationCompleted()
    {
        _view = new BasinOutputView(CreateHost, createOwnView: false);
        _view.HostReady += OnHostReady;
        _view.HostFailed += error =>
        {
            Log.Error($"the compositor host could not start: {error.Message}");
            _ = ShutdownAsync(1);
        };
        _window = new Window
        {
            Width = 1,
            Height = 1,
            Title = "Waylonia",
            Content = _view,
            WindowDecorations = WindowDecorations.None,
            ShowInTaskbar = false,
            ShowActivated = false,
            CanResize = false,
            Background = global::Avalonia.Media.Brushes.Transparent,
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
        };
        _window.Closing += (_, e) =>
        {
            if (!_shuttingDown)
            {
                e.Cancel = true;
                _ = ShutdownAsync(0);
            }
        };

        _channelClients.Removed += _ => Dispatcher.UIThread.Post(() =>
        {
            if (--_attachedClients == 0 && _channelPump is { } pump)
            {
                pump.Stop();
                _channelPump = null;
            }
        });
        _registry = new SessionRegistry(this);
        _registry.Changed += OnSessionsChanged;
        _registry.SessionEnded += OnSessionEnded;
        _run!.Store.Changed += () => Dispatcher.UIThread.Post(RefreshCatalog);
        _catalog = _run.Store.Load(Log);

        if (_run!.Host.Tray)
        {
            _trayMenu = new TrayMenu(LaunchFromTray, () => _ = ShutdownAsync(0));
            _trayMenu.ShowQuitOnly();
            _tray = new TrayIcon
            {
                Icon = new WindowIcon(typeof(WayloniaApp).Assembly.GetManifestResourceStream("Waylonia.Waylonia_Logo.png")!),
                ToolTipText = "Waylonia — starting…",
                Menu = _trayMenu.Menu,
            };
            TrayIcon.SetIcons(this, [_tray]);
        }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = _window;
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        }

        _signals.Add(System.Runtime.InteropServices.PosixSignalRegistration.Create(
            System.Runtime.InteropServices.PosixSignal.SIGINT, OnPosixSignal));
        _signals.Add(System.Runtime.InteropServices.PosixSignalRegistration.Create(
            System.Runtime.InteropServices.PosixSignal.SIGTERM, OnPosixSignal));

        base.OnFrameworkInitializationCompleted();
    }

    private void OnPosixSignal(System.Runtime.InteropServices.PosixSignalContext context)
    {
        context.Cancel = true;
        Dispatcher.UIThread.Post(() => _ = ShutdownAsync(0));
    }

    private BasinCompositorHost CreateHost()
    {
        var run = _run!;
        _textInput = new AvaloniaTextInput(action => _view!.Post(action));
        _xwayland = OperatingSystem.IsLinux() && run.Host.XWayland && run.LocalOnly
            ? WayloniaXWayland.TryCreateModule()
            : null;
        var host = new BasinCompositorHost(new BasinCompositorOptions
        {
            AppName = "waylonia",
            SocketName = run.SocketName,
            ManagedTransport = run.ManagedTransport,
            TextInput = _textInput,
            ExtraModules = _xwayland is { } xwayland ? [xwayland] : null,
        });
        _windows = new ToplevelWindows(host, action => _view!.Post(action), requestFrame: () => _view?.RequestFrame());
        _desktop = new DesktopShellPolicy(
            run.Host.FollowCursor ? new CursorScreenPolicy() : new AvaloniaShellPolicy())
        {
            Size = run.LocalDesktop?.Size,
        };
        if (host.Services.Find<Basin.Desktop.FullscreenShellGlobal>() is { } fullscreenShell)
        {
            _desktop.BoundClients = () => fullscreenShell.BoundClients;
        }

        if (run.ManagedTransport)
        {
            _wireClock = new WireClockClients();
            if (host.Services.Find<PresentationTimeGlobal>() is { } presentation)
            {
                presentation.WireClock = _wireClock;
            }

            if (host.Services.Find<Basin.Desktop.CommitTimingManager>() is { } timing)
            {
                timing.WireClock = _wireClock;
            }

            host.Display.SetGlobalFilter(_channelClients.Filter);
        }

        _windows.Policy = _desktop;
        _windows.ScreenWindowChanged += OnScreenWindowChanged;
        _windows.WindowOpened += OnWindowOpened;
        _windows.CountChanged += count => UpdateStatus($"{count} client window(s) on {host.Socket}");
        if (run.Host.Drag)
        {
            _hostDrag = new HostDrag(host);
            _windows.AttachDrag(_hostDrag);
        }

        _windows.AttachTextInput(_textInput);
        if (_xwayland is { } attachXwayland)
        {
            WayloniaXWayland.Attach(attachXwayland, host, _windows);
        }

        if (run.Host.Clipboard)
        {
            _clipboard = new HostClipboard(
                host,
                () => _window is { } window ? global::Avalonia.Controls.TopLevel.GetTopLevel(window)?.Clipboard : null,
                action => _view!.Post(action));
            _windows.WindowActivatedOnHost += () => _ = _clipboard!.PushFromHostAsync();
        }

        host.Composited += OnComposited;
        _host = host;
        return host;
    }

    private void OnWindowOpened(ToplevelWindow window, WlClient? client)
    {
        if (client is null || _channelClients.OwnerOf(client) is not WaypipeAcceptor { Session: not null } owner)
        {
            return;
        }

        var name = owner.Name;
        _sessionWindows[window] = name;
        window.Closed += (_, _) => _sessionWindows.Remove(window);
        if (_run!.Host.SessionTitles)
        {
            window.DecorateTitle(title => $"{title} — {name}");
        }
    }

    private void OnHostReady(BasinCompositorHost host)
    {
        var run = _run!;
        if (_window is { } window)
        {
            var screens = window.Screens;
            var scaleSettled = false;
            void Publish()
            {
                var snapshot = HostScreens.Capture(screens);
                var key = HostScreens.KeyFor(screens, screens.ScreenFromWindow(window) ?? screens.Primary);
                var scale = window.RenderScaling;
                var noteScale = scale > 0 && (scaleSettled || scale != 1.0);
                _view?.Post(() =>
                {
                    host.Screens.Apply(snapshot);
                    foreach (var info in snapshot)
                    {
                        if (HostScreenScales.TryGetScale(info) is { } known)
                        {
                            host.Screens.NoteWindowScale(info.Key, known);
                        }
                    }

                    if (key is not null && noteScale)
                    {
                        host.Screens.NoteWindowScale(key, scale);
                    }
                });
            }

            var probeDone = false;
            void HideProbe()
            {
                if (!probeDone)
                {
                    probeDone = true;
                    window.Hide();
                }
            }

            screens.Changed += (_, _) => Publish();
            window.ScalingChanged += (_, _) =>
            {
                scaleSettled = true;
                Publish();
                HideProbe();
            };
            Publish();
            if (window.RenderScaling != 1.0)
            {
                HideProbe();
            }
            else
            {
                DispatcherTimer.RunOnce(HideProbe, TimeSpan.FromMilliseconds(1500));
            }
        }

        BasinReport.Line(ReportLines.Socket(host.Socket));
        StartGlobalHotkeys();
        CreateCapture(host);

        if (WayloniaXWayland.DisplayName(host) is { } xdisplay)
        {
            BasinReport.Line($"XWAYLAND {xdisplay}");
            Environment.SetEnvironmentVariable("DISPLAY", xdisplay);
        }

        UpdateStatus(run.ManagedTransport ? "ready" : $"waiting for clients on {host.Socket}");
        if (LocalApplicationsWanted(host))
        {
            LoadLocalApplications();
        }

        if (run.LocalDesktop is { } local)
        {
            if (_desktop is not null)
            {
                _desktop.Size = local.Size ?? DesktopSize();
            }

            LaunchLocalDesktop(host, local);
        }
        else if (run.WaypipeListen is { } listen)
        {
            StartListening(listen);
        }

        foreach (var settings in run.Initial)
        {
            _ = ConnectSessionAsync(settings);
        }

        if (run.LocalCommand is { } command)
        {
            _localClient = BasinDiagnostics.StartClient(command, host.Socket);
            if (_localClient is null)
            {
                Log.Error($"failed to start '{command}'");
                _ = ShutdownAsync(1);
            }
        }

        RebuildTray();
        if (run.OpenSettings)
        {
            OpenSettings();
        }
    }

    private bool LocalApplicationsWanted(BasinCompositorHost host) =>
        _run!.Host.Tray && _run.Host.TrayApps && OperatingSystem.IsLinux() && host.Socket.Length > 0
        && _run.WaypipeListen is null && _run.LocalDesktop is null;

    private (int Width, int Height) DesktopSize()
    {
        var screen = HostCursor.TryGetPosition() is { } cursor && _window?.Screens is { } screens
            ? screens.ScreenFromPoint(cursor) ?? screens.Primary
            : _window?.Screens?.Primary;
        var scaling = screen?.Scaling is > 0 ? screen.Scaling : 1.0;
        return DesktopShellPolicy.DefaultSize(screen, scaling);
    }

    private void StartListening(ListenSettings listen)
    {
        var acceptor = new WaypipeAcceptor(
            listen.Endpoint, this, listen.Compression, listen.Gpu, listen.Video, listen.VideoDecoder, session: null);
        acceptor.Failed += failure => Dispatcher.UIThread.Post(() => _ = ShutdownAsync(1));
        _listen = acceptor;
        UpdateStatus($"waiting for a waypipe channel on {listen.Endpoint}");
        try
        {
            var endpoint = WaypipeAcceptor.ParseEndpoint(listen.Endpoint, out var error);
            if (endpoint is null)
            {
                Log.Error($"{error}");
                _ = ShutdownAsync(1);
                return;
            }

            acceptor.Accept(WaypipeAcceptor.Listen(endpoint));
        }
        catch (Exception error) when (error is System.Net.Sockets.SocketException or IOException or FormatException or UnauthorizedAccessException)
        {
            Log.Error($"the channel listener failed: {error.Message}");
            _ = ShutdownAsync(1);
        }
    }

    private void LaunchLocalDesktop(BasinCompositorHost host, LocalDesktop local)
    {
        var recipe = local.Recipe;
        var environment = DesktopSession.Environment(recipe, local.Env, local.Gpu);
        var wrapper = DesktopSession.Wrapper(recipe, host.Socket, recipe.Command, environment);
        Log.Debug($"starting the {recipe.Name} session on this machine");
        _localClient = BasinDiagnostics.StartClient(wrapper, host.Socket, [("DISPLAY", null)]);
        if (_localClient is null)
        {
            Log.Error($"the {recipe.Name} session failed to start");
            _ = ShutdownAsync(1);
            return;
        }

        if (_desktop is not null)
        {
            _desktop.DeclaredPid = _localClient.Id;
        }

        UpdateStatus($"starting {recipe.Name}");
    }

    private async Task<string?> ConnectSessionAsync(SessionSettings settings)
    {
        if (_shuttingDown || _registry is not { } registry)
        {
            return "waylonia is shutting down";
        }

        if (!_run!.ManagedTransport)
        {
            const string why = "this run binds the system libwayland socket for a local client, which carries no " +
                "waypipe channel; start waylonia without a command to connect sessions";
            Log.Error($"{why}");
            return why;
        }

        if (registry.WhyRefused(settings) is { } refused)
        {
            Log.Error($"{refused}");
            UpdateStatus(refused);
            return refused;
        }

        if (settings.IsDesktop && _desktop is { } desktop)
        {
            desktop.Size = settings.DesktopSize ?? DesktopSize();
        }

        var session = registry.Add(settings);
        return await session.ConnectAsync() ? null : session.LastError ?? $"{settings.Name} could not connect";
    }

    private Task<string?> ConnectProfileAsync(SessionProfile profile)
    {
        var resolved = SessionSettings.Resolve(
            profile, new SessionOverrides(AudioFormat: _run!.AudioFormat), _run.Config, Log);
        if (resolved.Settings is not { } settings)
        {
            Log.Error($"{resolved.Error}");
            return Task.FromResult<string?>(resolved.Error);
        }

        return ConnectSessionAsync(settings);
    }

    private async Task DisconnectSessionAsync(string name)
    {
        if (_registry is { } registry)
        {
            await registry.DisconnectAsync(name);
        }
    }

    private void OnSessionsChanged() => Dispatcher.UIThread.Post(() =>
    {
        if (_shuttingDown)
        {
            return;
        }

        if (_desktop is { HasClaimed: true } desktop
            && _registry is { } registry
            && !registry.Live.Any(static session => session.Settings.IsDesktop)
            && _run!.LocalDesktop is null)
        {
            desktop.Release();
        }

        RestartGlobalHotkeys();
        RebuildTray();
    });

    private void OnSessionEnded(SshSession session, int code) => Dispatcher.UIThread.Post(() =>
    {
        if (_shuttingDown || _registry is not { } registry)
        {
            return;
        }

        var othersLive = registry.Live.Any(other => !ReferenceEquals(other, session));
        if (SessionRegistry.ExitsProcess(session.Settings.AdHoc, session.HadClients, othersLive, _run!.Manager))
        {
            _ = ShutdownAsync(code);
        }
    });

    private void RefreshCatalog()
    {
        _catalog = _run!.Store.Load(Log);
        RebuildTray();
    }

    private void OpenManager()
    {
        if (_shuttingDown || _registry is not { } registry)
        {
            return;
        }

        if (_manager is null)
        {
            _manager = new ManagerWindow(
                new ManagerViewModel(
                    _run!.Store,
                    registry,
                    Log,
                    ConnectProfileAsync,
                    DisconnectSessionAsync,
                    _run.Config.Path is null ? null : OpenSettings));
        }
        else
        {
            _manager.Model.Reload();
        }

        _manager.Show();
        _manager.Activate();
    }

    private void OpenSettings()
    {
        if (_shuttingDown || _run!.Config.Path is not { } path)
        {
            return;
        }

        if (_settings is null)
        {
            _settings = new SettingsWindow(new SettingsViewModel(path, _startup ?? _run.Config.Values, Log, ApplyConfig));
        }
        else
        {
            _settings.Model.Reload();
        }

        _settings.Show();
        _settings.Activate();
    }

    private void ApplyConfig(Config config)
    {
        if (_shuttingDown)
        {
            return;
        }

        _run = _run! with { Host = config.Host, Config = config };
        foreach (var (window, name) in _sessionWindows)
        {
            window.DecorateTitle(config.Host.SessionTitles ? title => $"{title} — {name}" : null);
        }

        RestartGlobalHotkeys();
        _capture?.Dispose();
        _capture = null;
        if (_host is { } host)
        {
            CreateCapture(host);
            if (LocalApplicationsWanted(host))
            {
                LoadLocalApplications();
                return;
            }
        }

        _localApplications = null;
        _localNotice = null;
        RebuildTray();
    }

    private void CreateCapture(BasinCompositorHost host)
    {
        if (_windows is not { } windows
            || CaptureChord.Parse(_run!.Host.CaptureChord, BasinLog.For("waylonia")) is not { } chord)
        {
            return;
        }

        _capture = new CaptureToggle(chord, windows, _view!, host, ArmHotkeys);
        if (_screenWindow is { } window)
        {
            _capture.Attach(window, _screenTitle);
        }
    }

    private void ArmHotkeys(bool arm)
    {
        _hotkeysDisarmed = !arm;
        if (arm)
        {
            StartGlobalHotkeys();
        }
        else
        {
            _globalHotkeys?.Dispose();
            _globalHotkeys = null;
        }
    }

    private void LaunchHotkey(Hotkey hotkey)
    {
        var label = $"hotkey '{hotkey.Chord}'";
        if (hotkey.Session is { } name)
        {
            LaunchInSession(name, label, hotkey.Command);
        }
        else if (_registry?.AdHoc is { } adHoc)
        {
            _ = adHoc.LaunchAsync(hotkey.Command, label);
        }
        else
        {
            LaunchLocal(hotkey.Command, label);
        }
    }

    private void LaunchFromTray(string? session, string label, string command)
    {
        if (session is { } name)
        {
            LaunchInSession(name, label, command);
        }
        else
        {
            LaunchLocal(command, label);
        }
    }

    private void LaunchInSession(string name, string label, string command)
    {
        if (_shuttingDown || _registry is not { } registry)
        {
            return;
        }

        if (registry.Get(name) is { } session)
        {
            _ = session.LaunchAsync(command, label);
        }
        else if (_catalog.Find(name) is { } profile)
        {
            _ = ConnectThenLaunchAsync(profile, command, label);
        }
        else
        {
            Log.Warn($"{label}: no session is named {name}");
        }
    }

    private async Task ConnectThenLaunchAsync(SessionProfile profile, string command, string label)
    {
        if (await ConnectProfileAsync(profile) is null && _registry?.Get(profile.Name) is { } session)
        {
            await session.LaunchAsync(command, label);
        }
    }

    private void LaunchLocal(string command, string label)
    {
        if (_shuttingDown || _host is not { } host)
        {
            return;
        }

        if (host.Socket.Length == 0)
        {
            Log.Warn($"{label}: '{command}' has no local socket to start on");
            return;
        }

        try
        {
            if (BasinDiagnostics.StartClient(command, host.Socket) is null)
            {
                return;
            }
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Warn($"{label}: '{command}' failed to start: {error.Message}");
            return;
        }

        UpdateStatus($"started '{command}'");
    }

    private void LoadLocalApplications()
    {
        if (_trayMenu is null || _shuttingDown)
        {
            return;
        }

        _localNotice = "Loading applications…";
        RebuildTray();
        _ = LoadLocalApplicationsAsync();
    }

    private async Task LoadLocalApplicationsAsync()
    {
        var locale = DesktopLocale.FromEnvironment();
        var currentDesktop = ApplicationMenu.CurrentDesktop(_run!.Host.CurrentDesktop);
        var terminal = ApplicationMenu.Terminal(_run.Host.Terminal);
        IReadOnlyList<DesktopEntry> listable;
        try
        {
            listable = await Task.Run(() => new DesktopEntries(locale).Listable(currentDesktop));
        }
        catch (Exception error) when (error is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Warn($"the application list could not be read: {error.Message}");
            Dispatcher.UIThread.Post(() =>
            {
                _localNotice = "The applications could not be read";
                RebuildTray();
            });
            return;
        }

        var items = ApplicationMenu.Build(listable, terminal);
        if (terminal is null && listable.Any(static entry => entry.Terminal))
        {
            Log.Info($"terminal applications are left out of the tray menu; set terminal in the config to list them");
        }

        Log.Debug($"{listable.Count} application(s) in {items.Count} categor(ies) for the tray menu");
        Dispatcher.UIThread.Post(() =>
        {
            _localApplications = items;
            _localNotice = null;
            RebuildTray();
        });
    }

    private void RebuildTray()
    {
        if (_trayMenu is not { } menu || _shuttingDown || _registry is not { } registry)
        {
            return;
        }

        var run = _run!;
        var apps = run.Host.TrayApps;
        var entries = new List<SessionMenuEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var session in registry.Sessions)
        {
            seen.Add(session.Name);
            entries.Add(Entry(session.Name, session));
        }

        foreach (var profile in _catalog.Profiles)
        {
            if (seen.Add(profile.Name))
            {
                entries.Add(Entry(profile.Name, null));
            }
        }

        if (run.WaypipeListen is not null || run.LocalDesktop is not null)
        {
            entries.Clear();
        }

        var manager = run.ManagedTransport && run.WaypipeListen is null;
        menu.Show(SessionMenu.Build(
            entries,
            apps ? _localApplications : null,
            apps ? _localNotice : null,
            apps && _localApplications is not null || _localNotice is not null ? () => LoadLocalApplications() : null,
            manager ? OpenManager : null,
            run.Config.Path is null ? null : OpenSettings));

        SessionMenuEntry Entry(string name, SshSession? session) => new(
            name,
            session?.Status ?? SessionStatus.Disconnected,
            apps ? session?.Applications : null,
            apps ? session?.ApplicationsNotice : null,
            () => ConnectByName(name),
            () => _ = DisconnectSessionAsync(name),
            () => _ = session?.LoadApplicationsAsync() ?? Task.CompletedTask);
    }

    private void ConnectByName(string name)
    {
        if (_catalog.Find(name) is { } profile)
        {
            _ = ConnectProfileAsync(profile);
        }
        else if (_registry?.Get(name) is { } session)
        {
            _ = ConnectSessionAsync(session.Settings);
        }
    }

    private void OnScreenWindowChanged(ToplevelWindow? window)
    {
        if (window is null)
        {
            _screenWindow = null;
            _capture?.Detach();
            UpdateStatus("the desktop window is gone");
            return;
        }

        var title = window.Title ?? "desktop";
        if (_registry?.Live.FirstOrDefault(static session => session.Settings.IsDesktop) is { } desktopSession)
        {
            title = $"{desktopSession.Settings.Desktop!.Name} @ {desktopSession.Ssh}";
            window.OverrideTitle(title);
        }
        else if (_run!.LocalDesktop is { } local)
        {
            title = local.Recipe.Name;
            window.OverrideTitle(title);
        }

        _screenWindow = window;
        _screenTitle = title;
        _capture?.Attach(window, title);
        UpdateStatus($"the desktop window is up");
    }

    private void StartGlobalHotkeys()
    {
        if (_globalHotkeys is not null || _hotkeysDisarmed || _shuttingDown
            || _window is not { } anchor || _host is not { } host || _registry is not { } registry)
        {
            return;
        }

        var hotkeys = registry.Hotkeys(_run!.Host.Hotkeys);
        _hotkeySignature = Signature(hotkeys);
        if (hotkeys.Count == 0)
        {
            return;
        }

        if (host.Socket.Length == 0 && !registry.AnyLive)
        {
            Log.Warn($"this session has no local socket and no connected session, global hotkeys wait");
            return;
        }

        _globalHotkeys = GlobalHotkeys.TryStart(hotkeys, anchor, _view!, host, LaunchHotkey);
    }

    private void RestartGlobalHotkeys()
    {
        if (_registry is not { } registry || _hotkeysDisarmed)
        {
            return;
        }

        var signature = Signature(registry.Hotkeys(_run!.Host.Hotkeys, quiet: true));
        if (signature == _hotkeySignature && (_globalHotkeys is not null || signature.Length == 0))
        {
            return;
        }

        _globalHotkeys?.Dispose();
        _globalHotkeys = null;
        StartGlobalHotkeys();
    }

    private static string Signature(IReadOnlyList<Hotkey> hotkeys) =>
        string.Join('\n', hotkeys.Select(static hotkey => $"{hotkey.Modifiers}+{hotkey.Key}={hotkey.Session}:{hotkey.Command}"));

    private void UpdateStatus(string text)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_tray is not null)
            {
                _tray.ToolTipText = $"Waylonia — {text}";
            }
        });
    }

    private void OnComposited(long composited)
    {
        Interlocked.Exchange(ref _rendered, composited);
        var run = _run!;
        if (run.Frames > 0 && composited >= run.Frames && !_shuttingDown)
        {
            Dispatcher.UIThread.Post(() => _ = ShutdownAsync(0));
        }
    }

    [Conditional("DEBUG")]
    private static void Protocol(string line) => BasinReport.Line(line);

    private static void WriteScreenshot(BasinCompositorHost host, string path)
    {
        using var renderer = new Basin.Render.Skia.SkiaRenderer();
        var view = host.Session.Outputs.Count > 0 ? host.Session.Outputs[0] : null;
        var width = view?.Output.CurrentMode.Width ?? 1024;
        var height = view?.Output.CurrentMode.Height ?? 768;
        var shot = new MemoryBuffer(width, height, DrmFormat.Xrgb8888);
        var origin = view?.Position ?? default;
        host.Scene.Root.SetPosition(-origin.X, -origin.Y);
        try
        {
            host.Scene.Render(renderer, shot, new RenderColor(0.06f, 0.06f, 0.08f, 1f));
        }
        finally
        {
            host.Scene.Root.SetPosition(0, 0);
        }

        BufferCapture.WritePng(shot, path);
        shot.Destroy();
        BasinReport.Line($"SCREENSHOT {path}");
    }

    private async Task ShutdownAsync(int status)
    {
        if (_shuttingDown)
        {
            return;
        }

        _shuttingDown = true;
        _exitStatus = status;
        _capture?.Dispose();
        _capture = null;
        _globalHotkeys?.Dispose();
        _globalHotkeys = null;
        HostCursor.Close();
        if (_manager is { } manager)
        {
            manager.AllowClose();
            manager.Close();
            _manager = null;
        }

        if (_settings is { } settings)
        {
            settings.AllowClose();
            settings.Close();
            _settings = null;
        }

        if (_run!.Screenshot is { } path && _host is { } aliveHost && _view is { } pump)
        {
            var written = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            pump.Post(() =>
            {
                WriteScreenshot(aliveHost, path);
                written.TrySetResult();
            });
            pump.RequestFrame();
            await Task.WhenAny(written.Task, Task.Delay(2000));
        }

        var stopped = false;
        if (_registry is { } registry)
        {
            foreach (var session in registry.Sessions)
            {
                stopped |= session.StopClients();
            }
        }

        if (_localClient is { } client)
        {
            BasinDiagnostics.StopClient(client);
            stopped = true;
        }

        if (stopped)
        {
            for (var i = 0; i < 20 && _host is { Display.Clients.Count: > 0 }; i++)
            {
                _view?.RequestFrame();
                await Task.Delay(50);
            }
        }

        _channelPump?.Stop();
        _channelPump = null;
        await Task.Run(() => _registry?.DisposeAll());
        _listen?.Dispose();
        _mixer.Dispose();

        if (_view is { } integrationPump)
        {
            if (_clipboard is { } clipboard)
            {
                integrationPump.Post(clipboard.Dispose);
            }

            if (_hostDrag is { } hostDrag)
            {
                integrationPump.Post(hostDrag.Dispose);
            }
        }

        if (_windows is { } windows)
        {
            await windows.CloseAllAsync();
        }

        if (_view is { } view)
        {
            await view.ShutdownAsync();
        }

        Protocol(ReportLines.Frames(Rendered));
        if (BasinCounters.Enabled && (BasinCounters.LiveObjects != 0 || BasinCounters.PendingFrees != 0))
        {
            Log.Error(
                $"teardown not clean (live={BasinCounters.LiveObjects} pendingFrees={BasinCounters.PendingFrees})");
            Log.Error($"{BasinCounters.CensusReport()}");
            _exitStatus = 1;
        }

        _window?.Close();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }
}
