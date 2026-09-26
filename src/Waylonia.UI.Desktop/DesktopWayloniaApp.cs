using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Basin;
using Basin.Avalonia;
using Basin.Diagnostics;
using Basin.Freedesktop;
using Basin.Hosted;
using Basin.Scene;
using Basin.Shell.Nested;
using Basin.Ipc;
using Wayland.Server;
using Waylonia.Agent;
using Waylonia.Sessions;
using Waylonia.Shell;
using Waylonia.UI;
using static Waylonia.WayloniaLog;

namespace Waylonia;

internal sealed class DesktopWayloniaApp : WayloniaApp
{
    private static DesktopPlatform? _desktopPlatform;

    private readonly List<System.Runtime.InteropServices.PosixSignalRegistration> _signals = [];
    private readonly Dictionary<ToplevelWindow, string> _sessionWindows = [];
    private ToplevelWindows? _windows;
    private HostDrag? _hostDrag;
    private Process? _localClient;
    private Window? _window;
    private TrayIcon? _tray;
    private TrayMenu? _trayMenu;
    private IDisposable? _globalHotkeys;
    private string _hotkeySignature = string.Empty;
    private bool _hotkeysDisarmed;
    private DesktopShellPolicy? _desktop;
    private CaptureToggle? _capture;
    private IProtocolModule? _xwayland;
    private WaypipeAcceptor? _listen;
    private ManagerWindow? _manager;
    private SettingsWindow? _settings;
    private ToplevelWindow? _screenWindow;
    private string _screenTitle = string.Empty;
    private ShellWindow? _shellWindow;
    private AgentRuntime? _agent;
    private string _agentStatus = "Agent: driving";

    private static DesktopPlatform Desktop => _desktopPlatform!;

    private static bool TrayWanted => RunSettings.Host.Tray && RunSettings.Capabilities.Tray;

    public static int Run(WayloniaRun run, DesktopPlatform platform)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(platform);
        _desktopPlatform = platform;
        Prepare(run, platform.Base);
        var builder = platform.Base.Windowing.Configure(AppBuilder.Configure<DesktopWayloniaApp>());
        var status = builder.StartWithClassicDesktopLifetime([]);
        return status != 0 ? status : ExitStatus;
    }

    protected override void OnLifetimeReady()
    {
        _window = new Window
        {
            Width = 1,
            Height = 1,
            Title = "Waylonia",
            Content = OutputView,
            WindowDecorations = WindowDecorations.None,
            ShowInTaskbar = false,
            ShowActivated = false,
            CanResize = false,
            Background = global::Avalonia.Media.Brushes.Transparent,
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
        };
        _window.Closing += (_, e) =>
        {
            if (!ShuttingDown)
            {
                e.Cancel = true;
                _ = ShutdownAsync(0);
            }
        };

        if (TrayWanted)
        {
            _trayMenu = new TrayMenu(Launch, () => _ = ShutdownAsync(0));
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
        _signals.Add(System.Runtime.InteropServices.PosixSignalRegistration.Create(
            System.Runtime.InteropServices.PosixSignal.SIGHUP, OnPosixSignal));
        _agent = PreparedAgent;
    }

    public static AgentRuntime? PreparedAgent { get; set; }

    private bool AgentMode => RunSettings.Agent is not null;

    protected override void ConfigureServices(BasinCompositorHost host, BasinServices services)
    {
        base.ConfigureServices(host, services);
        if (OperatingSystem.IsLinux() && _agent is { } agent)
        {
            agent.ConfigureServices(host, services);
        }
    }

    protected override void OnHostInput(in BasinViewInput input)
    {
        if (OperatingSystem.IsLinux() && _agent is { } agent && AgentShell.TakesOver(input))
        {
            agent.HostInput(pressOrKey: true);
        }
    }

    protected override string ShellTitle(SessionRegistry registry) =>
        RunSettings.Agent is { } profile ? AgentShell.Title(profile, _agentStatus) : base.ShellTitle(registry);

    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    private void AttachAgent(NestedShell shell, BasinViewOutput view)
    {
        if (_agent is not { } agent || Host is not { } host || OutputView is not { } pump)
        {
            return;
        }

        agent.XDisplay = () => Desktop.XWayland.DisplayName(host);
        agent.Attach(host, view, shell, new VisibleAgentCompositor(pump, host, shell), () => Dispatcher.UIThread.Post(() => _ = ShutdownAsync(0)));
        agent.Takeover.Changed += () =>
        {
            var status = agent.Takeover.Status;
            var paused = agent.Takeover.IsPaused;
            Dispatcher.UIThread.Post(() => ShowAgentStatus(status, paused));
        };
        agent.Approvals.Requested += approval =>
        {
            var id = approval.Id;
            var prompt = agent.PromptFor(approval);
            Dispatcher.UIThread.Post(() =>
            {
                if (Chrome is not { } chrome)
                {
                    pump.Post(() => agent.Approvals.Answer(id, IpcApprovalAnswer.Deny));
                    return;
                }

                var close = chrome.Approve(prompt, choice => pump.Post(() => agent.Approvals.Answer(id, choice switch
                {
                    AgentApprovalChoice.AllowOnce => IpcApprovalAnswer.AllowOnce,
                    AgentApprovalChoice.AllowRun => IpcApprovalAnswer.AllowRun,
                    _ => IpcApprovalAnswer.Deny,
                })));
                pump.Post(() =>
                {
                    if (approval.IsAnswered)
                    {
                        Dispatcher.UIThread.Post(close);
                    }
                    else
                    {
                        approval.Answered += _ => Dispatcher.UIThread.Post(close);
                    }
                });
            });
        };
    }

    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    private async Task DetachAgentAsync(AgentRuntime agent)
    {
        var detached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (OutputView is { } loop)
        {
            loop.Post(() =>
            {
                agent.Detach();
                detached.TrySetResult();
            });
            await Task.WhenAny(detached.Task, Task.Delay(5000));
        }

        await Task.Run(agent.Dispose);
    }

    private void ShowAgentStatus(string status, bool paused)
    {
        _agentStatus = status;
        _shellWindow?.SetAgentStatus(status, paused);
        RefreshShellTitle();
    }

    private void OnPosixSignal(System.Runtime.InteropServices.PosixSignalContext context)
    {
        context.Cancel = true;
        Dispatcher.UIThread.Post(() => _ = ShutdownAsync(0));
    }

    protected override IReadOnlyList<IProtocolModule>? ExtraModules()
    {
        var run = RunSettings;
        _xwayland = run.Capabilities.XWayland && run.Host.XWayland && run.WaypipeListen is null
            ? Desktop.XWayland.TryCreateModule()
            : null;
        return _xwayland is { } xwayland ? [xwayland] : null;
    }

    protected override BasinCompositorHost CreateWindowsHost(BasinCompositorHost host, WayloniaRun run)
    {
        _windows = new ToplevelWindows(host, action => OutputView!.Post(action), requestFrame: () => OutputView?.RequestFrame());
        _desktop = new DesktopShellPolicy(
            run.Host.FollowCursor ? new CursorScreenPolicy(Platform.Cursor) : new AvaloniaShellPolicy(),
            Platform.Cursor)
        {
            Size = run.LocalDesktop?.Size,
        };
        if (host.Services.Find<Basin.Desktop.FullscreenShellGlobal>() is { } fullscreenShell)
        {
            _desktop.BoundClients = () => fullscreenShell.BoundClients;
        }

        WireTransport(host, run);
        _windows.Policy = _desktop;
        _windows.ScreenWindowChanged += OnScreenWindowChanged;
        _windows.WindowOpened += OnWindowOpened;
        _windows.CountChanged += count => UpdateStatus($"{count} client window(s) on {host.Socket}");
        if (run.Host.Drag)
        {
            _hostDrag = new HostDrag(host);
            _windows.AttachDrag(_hostDrag);
        }

        _windows.AttachTextInput(TextInput!);
        if (_xwayland is { } attachXwayland)
        {
            Desktop.XWayland.Attach(attachXwayland, host, _windows);
        }

        if (run.Host.Clipboard)
        {
            var clipboard = CreateClipboard(
                host,
                () => _window is { } window ? TopLevel.GetTopLevel(window)?.Clipboard : null);
            _windows.WindowActivatedOnHost += () => _ = clipboard.PushFromHostAsync();
        }

        AdoptHost(host);
        return host;
    }

    protected override void AttachShell(NestedShell shell, BasinViewOutput view)
    {
        if (_xwayland is { } xwayland)
        {
            Desktop.XWayland.AttachShell(xwayland, shell, IconCache, Log);
        }

        if (OperatingSystem.IsLinux())
        {
            AttachAgent(shell, view);
        }
    }

    protected override IShellHost HostShell(BasinCompositorHost host, ShellWindowState state, Func<BasinCompositorHost, BasinViewOutput> createView)
    {
        var profile = RunSettings.Agent;
        if (profile is not null)
        {
            state = new ShellWindowState(profile.Width, profile.Height, null, null, false);
        }

        var window = new ShellWindow(host, state, createView);
        _shellWindow = window;
        if (profile is not null)
        {
            window.FixForAgent(profile.Width, profile.Height, () =>
            {
                if (OperatingSystem.IsLinux() && _agent is { } agent)
                {
                    OutputView?.Post(agent.Resume);
                }
            });
            window.SetAgentStatus(_agentStatus, paused: false);
            window.CloseRequested += () => _ = ShutdownAsync(0);
            return window;
        }

        window.CloseRequested += () =>
        {
            if (TrayWanted && _tray is not null)
            {
                window.Hide();
            }
            else
            {
                _ = ShutdownAsync(0);
            }
        };
        window.StateChanged += persisted => ShellStateFile.Save(RunSettings.Paths.StateFile, persisted, Log);
        return window;
    }

    protected override void OnShellReady(BasinCompositorHost host)
    {
        if (!AgentMode)
        {
            CreateCapture(host);
        }
    }

    protected override void OnChannelAttached(WaypipeAcceptor owner, WlClient client)
    {
        if (owner.Session is { IsDesktop: true } && _desktop is { HasClaimed: false } desktop)
        {
            desktop.Declare(client);
        }
    }

    protected override void OnHostReadyCore(BasinCompositorHost host)
    {
        var run = RunSettings;
        if (_window is { } window && !Nested)
        {
            var screens = window.Screens;
            var scaleSettled = false;
            void Publish()
            {
                var snapshot = AvaloniaScreens.Capture(screens);
                var key = AvaloniaScreens.KeyFor(screens, screens.ScreenFromWindow(window) ?? screens.Primary);
                var scale = window.RenderScaling;
                var noteScale = scale > 0 && (scaleSettled || scale != 1.0);
                OutputView?.Post(() =>
                {
                    host.Screens.Apply(snapshot);
                    foreach (var info in snapshot)
                    {
                        if (Platform.ScreenScales.TryGetScale(info) is { } known)
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

        if (!AgentMode)
        {
            StartGlobalHotkeys();
        }

        if (!Nested)
        {
            CreateCapture(host);
        }

        if (Desktop.XWayland.DisplayName(host) is { } xdisplay)
        {
            BasinReport.Line($"XWAYLAND {xdisplay}");
            if (!AgentMode)
            {
                Environment.SetEnvironmentVariable("DISPLAY", xdisplay);
            }
        }

        if (!AgentMode && LocalApplicationsWanted(host))
        {
            LoadLocalApplications();
        }

        if (Nested)
        {
            OpenShell(host);
            RebuildTray();
            return;
        }

        StartInitialWork(host);
        RebuildTray();
        if (run.OpenSettings)
        {
            OpenSettings();
        }
    }

    protected override void StartInitialWork(BasinCompositorHost host)
    {
        var run = RunSettings;
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

        base.StartInitialWork(host);

        if (run.LocalCommand is { } command)
        {
            _localClient = BasinDiagnostics.StartClient(command, host.Socket);
            if (_localClient is null)
            {
                Log.Error($"failed to start '{command}'");
                _ = ShutdownAsync(1);
            }
        }
    }

    private bool LocalApplicationsWanted(BasinCompositorHost host)
    {
        var run = RunSettings;
        return (Nested || (TrayWanted && run.Host.TrayApps)) && run.Capabilities.LocalApplications && host.Socket.Length > 0
            && run.WaypipeListen is null && run.LocalDesktop is null;
    }

    private (int Width, int Height) DesktopSize()
    {
        var screen = Platform.Cursor.TryGetPixelPoint() is { } cursor && _window?.Screens is { } screens
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

    protected override void PrepareDesktopSession(SessionSettings settings)
    {
        if (_desktop is { } desktop)
        {
            desktop.Size = settings.DesktopSize ?? DesktopSize();
        }
    }

    protected override void OnRegistryChanged()
    {
        if (_desktop is { HasClaimed: true } desktop
            && Registry is { } registry
            && !registry.Live.Any(static session => session.Settings.IsDesktop)
            && RunSettings.LocalDesktop is null)
        {
            desktop.Release();
        }

        RestartGlobalHotkeys();
        RebuildTray();
    }

    protected override void OnCatalogChanged() => RebuildTray();

    protected override void OpenManagerOutsideShell(ManagerViewModel model)
    {
        _manager ??= new ManagerWindow(model);
        _manager.Show();
        _manager.Activate();
    }

    protected override void OpenSettingsOutsideShell(SettingsViewModel model)
    {
        _settings ??= new SettingsWindow(model);
        _settings.Show();
        _settings.Activate();
    }

    protected override Task<string?> AskOutsideShellAsync(string prompt, AskPassKind kind, CancellationToken cancellation)
    {
        var answered = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var window = new AskPassWindow(prompt, kind);
        window.Closed += (_, _) => answered.TrySetResult(window.Answer);
        window.Show();
        window.Activate();
        return answered.Task;
    }

    protected override void OnConfigApplied(Config config)
    {
        foreach (var (window, name) in _sessionWindows)
        {
            window.DecorateTitle(config.Host.SessionTitles ? title => $"{title} — {name}" : null);
        }

        RestartGlobalHotkeys();
        _capture?.Dispose();
        _capture = null;
        if (Host is { } host)
        {
            CreateCapture(host);
            if (LocalApplicationsWanted(host))
            {
                LoadLocalApplications();
                return;
            }
        }

        LocalApplications = null;
        LocalNotice = null;
        RebuildTray();
    }

    private void CreateCapture(BasinCompositorHost host)
    {
        var run = RunSettings;
        if (!run.Capabilities.KeyboardCapture
            || CaptureChord.Parse(run.Host.CaptureChord, BasinLog.For("waylonia")) is not { } chord)
        {
            return;
        }

        _capture = new CaptureToggle(chord, OutputView!, host, Desktop.Capture, ArmHotkeys);
        if (_shellWindow is { View.Toplevel: { } target } shell)
        {
            _capture.Attach(target, shell, "Waylonia", title => shell.SetTitle(title));
        }
        else if (_screenWindow is { } window)
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
        else if (Registry?.AdHoc is { } adHoc)
        {
            _ = adHoc.LaunchAsync(hotkey.Command, label);
        }
        else
        {
            LaunchLocal(hotkey.Command, label);
        }
    }

    protected override void LaunchLocal(string command, string label)
    {
        if (ShuttingDown || Host is not { } host)
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
        if (_trayMenu is null || ShuttingDown)
        {
            return;
        }

        LocalNotice = "Loading applications…";
        RebuildTray();
        _ = LoadLocalApplicationsAsync();
    }

    private async Task LoadLocalApplicationsAsync()
    {
        var locale = DesktopLocale.FromEnvironment();
        var currentDesktop = ApplicationMenu.CurrentDesktop(RunSettings.Host.CurrentDesktop);
        var terminal = ApplicationMenu.Terminal(RunSettings.Host.Terminal);
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
                LocalNotice = "The applications could not be read";
                RebuildTray();
            });
            return;
        }

        var items = ApplicationMenu.Build(
            listable,
            terminal,
            entry => HostIcons.ForName(entry.Icon) ?? (Nested ? Bluecurve.ForEntry(entry) : null),
            Nested ? category => Bluecurve.Path(BluecurveIconSource.CategoryIcon(category)) : null);
        if (terminal is null && listable.Any(static entry => entry.Terminal))
        {
            Log.Info($"set terminal in the config to list terminal applications");
        }

        Log.Debug($"{listable.Count} application(s) in {items.Count} categor(ies) for the tray menu");
        Dispatcher.UIThread.Post(() =>
        {
            LocalApplications = items;
            LocalNotice = null;
            RebuildTray();
            RefreshPanelApplications();
        });
    }

    private void RebuildTray()
    {
        if (_trayMenu is not { } menu || ShuttingDown || Registry is not { } registry)
        {
            return;
        }

        if (Nested)
        {
            menu.Show(SessionMenu.BuildNested(ShowShell));
            return;
        }

        var run = RunSettings;
        var apps = run.Host.TrayApps;
        var entries = new List<SessionMenuEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var session in registry.Sessions)
        {
            seen.Add(session.Name);
            entries.Add(Entry(session.Name, session));
        }

        foreach (var profile in Catalog.Profiles)
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
            apps ? LocalApplications : null,
            apps ? LocalNotice : null,
            apps && LocalApplications is not null || LocalNotice is not null ? () => LoadLocalApplications() : null,
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

    private void OnWindowOpened(ToplevelWindow window, WlClient? client)
    {
        if (client is null || ChannelClients.OwnerOf(client) is not WaypipeAcceptor { Session: not null } owner)
        {
            return;
        }

        var name = owner.Name;
        _sessionWindows[window] = name;
        window.Closed += (_, _) => _sessionWindows.Remove(window);
        if (RunSettings.Host.SessionTitles)
        {
            window.DecorateTitle(title => $"{title} — {name}");
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
        if (Registry?.Live.FirstOrDefault(static session => session.Settings.IsDesktop) is { } desktopSession)
        {
            title = $"{desktopSession.Settings.Desktop!.Name} @ {desktopSession.Ssh}";
            window.OverrideTitle(title);
        }
        else if (RunSettings.LocalDesktop is { } local)
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
        if (_globalHotkeys is not null || _hotkeysDisarmed || ShuttingDown || !RunSettings.Capabilities.GlobalHotkeys
            || _window is not { } anchor || Host is not { } host || Registry is not { } registry)
        {
            return;
        }

        var hotkeys = registry.Hotkeys(RunSettings.Host.Hotkeys);
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

        _globalHotkeys = Desktop.Hotkeys.TryStart(hotkeys, anchor, OutputView!, host, LaunchHotkey);
    }

    private void RestartGlobalHotkeys()
    {
        if (Registry is not { } registry || _hotkeysDisarmed)
        {
            return;
        }

        var signature = Signature(registry.Hotkeys(RunSettings.Host.Hotkeys, quiet: true));
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

    protected override void UpdateStatus(string text)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_tray is not null)
            {
                _tray.ToolTipText = $"Waylonia — {text}";
            }
        });
    }

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
            host.Scene.Render(renderer, shot, new RenderColor(0.06f, 0.06f, 0.08f, 1f), view?.Output.Scale ?? 1.0);
        }
        finally
        {
            host.Scene.Root.SetPosition(0, 0);
        }

        BufferCapture.WritePng(shot, path);
        shot.Destroy();
        BasinReport.Line($"SCREENSHOT {path}");
    }

    protected override async Task OnShuttingDownAsync()
    {
        if (OperatingSystem.IsLinux() && _agent is { } agent)
        {
            await DetachAgentAsync(agent);
            _agent = null;
        }

        _capture?.Dispose();
        _capture = null;
        _globalHotkeys?.Dispose();
        _globalHotkeys = null;
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

        if (RunSettings.Screenshot is { } path && Host is { } aliveHost && OutputView is { } pump)
        {
            var written = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (_shellWindow is { View.Toplevel: { } shotView })
            {
                pump.Post(() => aliveHost.RequestScreenshot(path, ok =>
                {
                    BasinReport.Line(ok ? $"SCREENSHOT {path}" : $"SCREENSHOT failed");
                    written.TrySetResult();
                }));
                shotView.RequestRender();
            }
            else
            {
                pump.Post(() =>
                {
                    WriteScreenshot(aliveHost, path);
                    written.TrySetResult();
                });
                pump.RequestFrame();
            }

            await Task.WhenAny(written.Task, Task.Delay(3000));
        }
    }

    protected override bool StopLocalClients()
    {
        if (_localClient is not { } client)
        {
            return false;
        }

        BasinDiagnostics.StopClient(client);
        return true;
    }

    protected override async Task CloseHostWindowsAsync()
    {
        _listen?.Dispose();
        if (OutputView is { } pump && _hostDrag is { } hostDrag)
        {
            pump.Post(hostDrag.Dispose);
        }

        if (_windows is { } windows)
        {
            await windows.CloseAllAsync();
        }

        _shellWindow = null;
    }

    protected override void ExitProcess(int status)
    {
        _window?.Close();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }
}
