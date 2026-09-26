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
using Basin.Shell.Nested;
using Wayland.Server;
using Waylonia.Audio;
using Waylonia.Cli;
using Waylonia.Sessions;
using Waylonia.Shell;
using Waylonia.UI;
using static Waylonia.WayloniaLog;

namespace Waylonia;

internal class WayloniaApp : Application, ISessionHost, ISshPrompter
{
    private static WayloniaRun? _run;
    private static HostPlatform? _platform;
    private static ConfigValues? _startup;
    private static int _exitStatus;
    private static long _rendered;

    private static readonly TimeSpan ResumeWindow = TimeSpan.FromSeconds(30);

    private readonly ChannelClients _channelClients = new();
    private readonly AudioMixer _mixer = new(static fill => _platform!.OpenAudio(fill, AudioMixer.Rate, AudioMixer.Channels));
    private readonly HostIcons _hostIcons = new();
    private readonly IconCache _iconCache = new(_platform!.Paths.IconCacheRoot);
    private readonly List<string> _resumePlan = [];
    private BasinOutputView? _view;
    private BasinCompositorHost? _host;
    private HostClipboard? _clipboard;
    private AvaloniaTextInput? _textInput;
    private WireClockClients? _wireClock;
    private DispatcherTimer? _channelPump;
    private int _attachedClients;
    private bool _suspended;
    private SessionRegistry? _registry;
    private SessionCatalog _catalog = SessionCatalog.Empty;
    private bool _shuttingDown;
    private NestedShell? _shell;
    private PanelArrangement? _panelArrangement;
    private IShellHost? _shellHost;
    private ShellView? _shellView;
    private ShellChrome? _chrome;
    private PanelModel? _panelModel;
    private bool _shellStarted;
    private bool _dragUnsupportedReported;
    private BluecurveIconSource? _bluecurve;
    private ManagerViewModel? _managerModel;
    private SettingsViewModel? _settingsModel;

    IconCache? ISessionHost.Icons => _iconCache;

    protected static WayloniaRun RunSettings => _run!;

    protected static HostPlatform Platform => _platform!;

    protected static ConfigValues? Startup => _startup;

    protected static int ExitStatus
    {
        get => _exitStatus;
        set => _exitStatus = value;
    }

    protected BasinOutputView? OutputView => _view;

    protected BasinCompositorHost? Host => _host;

    protected AvaloniaTextInput? TextInput => _textInput;

    protected ChannelClients ChannelClients => _channelClients;

    protected SessionRegistry? Registry => _registry;

    protected SessionCatalog Catalog => _catalog;

    protected NestedShell? Shell => _shell;

    protected IShellHost? ShellHost => _shellHost;

    protected ShellChrome? Chrome => _chrome;

    protected PanelModel? PanelModel => _panelModel;

    protected ManagerViewModel? ManagerModel => _managerModel;

    protected SettingsViewModel? SettingsModel => _settingsModel;

    protected HostIcons HostIcons => _hostIcons;

    protected IconCache IconCache => _iconCache;

    protected bool ShuttingDown => _shuttingDown;

    protected IReadOnlyList<ApplicationMenuItem>? LocalApplications { get; set; }

    protected string? LocalNotice { get; set; }

    protected BluecurveIconSource Bluecurve => _bluecurve ??= new BluecurveIconSource(_iconCache, Log);

    string? ISessionHost.ThemeIconFor(DesktopEntry entry) => Nested ? Bluecurve.ForEntry(entry) : null;

    string? ISessionHost.ThemeIconFor(DesktopMainCategory category) =>
        Nested ? Bluecurve.Path(BluecurveIconSource.CategoryIcon(category)) : null;

    private PanelIcons PanelIconsFor() => new(
        Bluecurve.Path(BluecurveIconSource.SessionsIcon),
        Bluecurve.Path(BluecurveIconSource.SettingsIcon),
        Bluecurve.Path(BluecurveIconSource.DisconnectIcon),
        Bluecurve.Path(BluecurveIconSource.QuitIcon),
        Bluecurve.Path("icon-computer"),
        path => Bluecurve.Path(BluecurveIconSource.PlaceIcon(path)),
        Bluecurve.Path(BluecurveIconSource.KeyboardIcon));

    private void ResolveIcon(string? session, string appId, string? clientIcon, Action<string?> resolved)
    {
        if (session is null || _registry?.Get(session) is not { } ssh)
        {
            Task.Run(() => resolved(
                _hostIcons.ForName(clientIcon) ?? _hostIcons.ForAppId(appId) ?? Bluecurve.ForWindow(appId, clientIcon) ?? Bluecurve.Fallback()));
            return;
        }

        var names = new List<string>();
        if (clientIcon is { Length: > 0 })
        {
            names.Add(clientIcon);
        }

        if (ssh.IconNameForAppId(appId) is { Length: > 0 } entryIcon)
        {
            names.Add(entryIcon);
        }

        names.Add(appId);
        foreach (var name in names)
        {
            if (ssh.IconPathFor(name) is { } cached)
            {
                resolved(cached);
                return;
            }
        }

        _ = ssh.FetchIconsAsync(names).ContinueWith(
            _ =>
            {
                foreach (var name in names)
                {
                    if (ssh.IconPathFor(name) is { } fetched)
                    {
                        resolved(fetched);
                        return;
                    }
                }

                resolved(
                    _hostIcons.ForName(clientIcon) ?? _hostIcons.ForAppId(appId) ?? Bluecurve.ForWindow(appId, clientIcon) ?? Bluecurve.Fallback());
            },
            TaskScheduler.Default);
    }

    protected bool Nested => _run!.Host.Shell == ShellMode.Nested;

    ISshLinkFactory ISessionHost.Links => _platform!.Links;

    ISshPrompter ISessionHost.Prompter => this;

    public static long Rendered => Interlocked.Read(ref _rendered);

    public override void Initialize() => Styles.Add(new global::BluerCurve.BluerCurveTheme());

    public static void Prepare(WayloniaRun run, HostPlatform platform)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(platform);
        _run = run;
        _platform = platform;
        _startup = run.Config.Values;
        _exitStatus = 0;
        SessionSettings.CreateDecoder = platform.Video.Create;
        global::Avalonia.Logging.Logger.Sink ??= new AvaloniaLogSink();
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
        OnChannelAttached(owner, client);
        Dispatcher.UIThread.Post(() =>
        {
            _attachedClients++;
            EnsurePump();
        });
        Protocol($"CHANNEL {owner.Name} {owner.Attached} attached");
    }

    protected virtual void OnChannelAttached(WaypipeAcceptor owner, WlClient client)
    {
    }

    private void EnsurePump()
    {
        if (_attachedClients <= 0 || _suspended || _shuttingDown)
        {
            return;
        }

        _channelPump ??= new DispatcherTimer(
            TimeSpan.FromMilliseconds(16), DispatcherPriority.Background, (_, _) => _view?.RequestFrame());
        if (!_channelPump.IsEnabled)
        {
            _channelPump.Start();
        }
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

        if (ApplicationLifetime is IActivityApplicationLifetime activity)
        {
            _shellView = new ShellView(_view);
            activity.MainViewFactory = () => _shellView;
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime single)
        {
            _shellView = new ShellView(_view);
            single.MainView = _shellView;
        }

        if (_shellView is not null && this.TryGetFeature<IActivatableLifetime>() is { } activatable)
        {
            activatable.Deactivated += (_, e) => OnActivation(e.Kind, active: false);
            activatable.Activated += (_, e) => OnActivation(e.Kind, active: true);
        }

        OnLifetimeReady();
        base.OnFrameworkInitializationCompleted();
    }

    protected virtual void OnLifetimeReady()
    {
    }

    private void OnActivation(ActivationKind kind, bool active)
    {
        if (kind != ActivationKind.Background || _shuttingDown)
        {
            return;
        }

        if (active)
        {
            Resume();
        }
        else
        {
            Suspend();
        }
    }

    private void Suspend()
    {
        if (_suspended)
        {
            return;
        }

        _suspended = true;
        _channelPump?.Stop();
        _view?.Post(() => _host?.Suspend());
        _resumePlan.Clear();
        if (_run!.Capabilities.Reconnects && _registry is { } registry)
        {
            _resumePlan.AddRange(ResumePolicy.Plan(registry.Live.Select(static session => session.Name), _catalog));
        }

        Log.Debug($"the host went to the background, {_resumePlan.Count} session(s) will reconnect on return");
    }

    private void Resume()
    {
        if (!_suspended)
        {
            return;
        }

        _suspended = false;
        _view?.Post(() => _host?.Resume());
        EnsurePump();
        foreach (var name in _resumePlan.ToArray())
        {
            if (_registry?.Get(name) is { IsLive: true })
            {
                continue;
            }

            _resumePlan.Remove(name);
            if (_catalog.Find(name) is { } profile)
            {
                _ = ConnectProfileAsync(profile);
            }
        }

        if (_resumePlan.Count > 0)
        {
            DispatcherTimer.RunOnce(_resumePlan.Clear, ResumeWindow);
        }
    }

    private BasinCompositorHost CreateHost()
    {
        var run = _run!;
        _textInput = new AvaloniaTextInput(action => _view!.Post(action));
        var host = new BasinCompositorHost(new BasinCompositorOptions
        {
            AppName = "waylonia",
            SocketName = run.SocketName,
            ManagedTransport = run.ManagedTransport,
            TextInput = _textInput,
            ExtraModules = ExtraModules(),
            ConfigureServices = ConfigureServices,
            Dmabuf = run.Agent is null,
        });
        return Nested ? CreateNestedHost(host, run) : CreateWindowsHost(host, run);
    }

    protected virtual IReadOnlyList<IProtocolModule>? ExtraModules() => null;

    protected virtual void ConfigureServices(BasinCompositorHost host, BasinServices services) =>
        VirtualInputGlobals.Apply(services, _run!.Host.VirtualInput);

    protected virtual BasinCompositorHost CreateWindowsHost(BasinCompositorHost host, WayloniaRun run) =>
        throw new InvalidOperationException("windows mode needs a desktop host; this host runs the nested shell only");

    protected void WireTransport(BasinCompositorHost host, WayloniaRun run)
    {
        if (!run.ManagedTransport)
        {
            return;
        }

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

    protected void AdoptHost(BasinCompositorHost host)
    {
        host.Composited += OnComposited;
        _host = host;
    }

    protected HostClipboard CreateClipboard(BasinCompositorHost host, Func<global::Avalonia.Input.Platform.IClipboard?> clipboard) =>
        _clipboard = new HostClipboard(host, clipboard, action => _view!.Post(action));

    private BasinCompositorHost CreateNestedHost(BasinCompositorHost host, WayloniaRun run)
    {
        WireTransport(host, run);
        if (run.Host.Drag && !_dragUnsupportedReported)
        {
            _dragUnsupportedReported = true;
            Log.Info($"host drag and drop is not yet supported in the nested shell");
        }

        if (run.Host.Clipboard)
        {
            CreateClipboard(host, () => _shellHost?.TopLevel?.Clipboard);
        }

        AdoptHost(host);
        return host;
    }

    private BasinViewOutput CreateShellView(BasinCompositorHost host, int width, int height, double scale)
    {
        var run = _run!;
        var view = host.CreateViewOutput(Math.Max(1, width), Math.Max(1, height), scale, NestedShell.OutputKey);
        var agent = run.Agent;
        var keys = agent is null ? KeyTable.Build(run.Config.ShellSettings.Keys, Hotkey.Reserved(run.Host.Hotkeys)) : KeyTable.Empty;
        _panelArrangement = agent is null ? PanelArrangement.From(run.Config.Panel, run.Capabilities, Log) : Agent.AgentShell.Arrangement;
        var shell = new NestedShell(
            host,
            view,
            agent is null ? run.Config.ShellSettings : Agent.AgentShell.Settings(agent, run.Config.ShellSettings),
            _panelArrangement.Layout,
            keys,
            action => _view!.Post(action),
            [BundledThemes.Load])
        {
            WindowSuffix = run.Host.SessionTitles ? SessionNameOf : null,
            ResolveIcon = ResolveIcon,
        };
        AttachShell(shell, view);
        shell.Changed += PublishPanelModel;
        shell.CursorChanged += name => Dispatcher.UIThread.Post(() => _shellHost?.View.ApplyCursor(CursorNames.For(name)));
        shell.ClientCursorChanged += cursor =>
        {
            if (ShellCursors.TryToAvalonia(cursor, out var avalonia))
            {
                Dispatcher.UIThread.Post(() => _shellHost?.View.ApplyCursor(avalonia));
            }
        };
        shell.SwitcherShown += (order, index) => Dispatcher.UIThread.Post(() => _chrome?.ShowSwitcher(order, index));
        shell.SwitcherMoved += index => Dispatcher.UIThread.Post(() => _chrome?.MoveSwitcher(index));
        shell.SwitcherHidden += () => Dispatcher.UIThread.Post(() => _chrome?.HideSwitcher());
        shell.MainMenuRequested += () => Dispatcher.UIThread.Post(() => _chrome?.OpenMainMenu());
        shell.HostFullScreenRequested += () => Dispatcher.UIThread.Post(ToggleShellFullScreen);
        shell.PanelsChanged += () => Dispatcher.UIThread.Post(() => _chrome?.Relayout());
        host.Session.BeforeDispatch += _ => shell.Tick(Environment.TickCount64);
        _shell = shell;
        Dispatcher.UIThread.Post(OnShellCreated);
        return view;
    }

    protected virtual void AttachShell(NestedShell shell, BasinViewOutput view)
    {
    }

    protected virtual void OnHostInput(in BasinViewInput input)
    {
    }

    protected void OpenShell(BasinCompositorHost host)
    {
        var state = ShellStateFile.Load(_run!.Paths.StateFile, Log);
        var shellHost = HostShell(host, state, h =>
        {
            var (width, height, scale) = _shellHost?.View.OutputSize(state.Width, state.Height) ?? (state.Width, state.Height, 1.0);
            return CreateShellView(h, width, height, scale);
        });
        _shellHost = shellHost;
        var view = shellHost.View;
        view.OutputResized += (width, height, scale) => _view?.Post(() => _shell?.Resize(width, height, scale));
        view.SoftKeyboardChanged += open =>
        {
            if (_panelModel is { } model)
            {
                model.SoftKeyboardOpen = open;
            }
        };
        view.SoftKeyboard = show => _textInput?.ShowSoftKeyboard(show);
        shellHost.ActivatedOnHost += () =>
        {
            if (_clipboard is { } clipboard)
            {
                _ = clipboard.PushFromHostAsync();
            }
        };
        if (view.Toplevel is { } toplevel)
        {
            toplevel.InputSink = input =>
            {
                OnHostInput(input);
                _shell?.HandleInput(input);
            };
            _textInput?.AttachView(toplevel);
        }

        shellHost.Present();
        UpdateShellTitle();
    }

    protected virtual IShellHost HostShell(BasinCompositorHost host, ShellWindowState state, Func<BasinCompositorHost, BasinViewOutput> createView)
    {
        if (_shellView is not { } view)
        {
            throw new InvalidOperationException("the nested shell needs a single view or a host window to live in");
        }

        view.AttachHost(host, createView);
        return new SingleViewShellHost(view, this.TryGetFeature<IActivatableLifetime>());
    }

    public void PressKey(uint evdev)
    {
        if (_shuttingDown || _shell is null)
        {
            return;
        }

        _shellHost?.View.PressKey(evdev);
    }

    private void OnShellCreated()
    {
        if (_shuttingDown || _shell is not { } shell || _shellHost is not { } shellHost || _host is not { } host)
        {
            return;
        }

        _panelModel ??= new PanelModel(new PanelCommands(
            () => _shell,
            action => _view!.Post(action),
            Launch,
            OpenManager,
            OpenSettings,
            name => _ = DisconnectSessionAsync(name),
            Quit,
            () => _shellHost?.View.ToggleSoftKeyboard()))
        {
            SettingsAvailable = _run!.Config.Path is not null,
            Icons = PanelIconsFor(),
            SoftKeyboardOpen = shellHost.View.SoftKeyboardOpen,
        };
        try
        {
            _chrome = new ShellChrome(shell, shellHost, _panelModel, _panelArrangement!, action => _view!.Post(action), Log);
        }
        catch (Exception error) when (error is InvalidOperationException or NotSupportedException)
        {
            Log.Error($"the shell panels could not be created: {error.Message}");
        }

        shellHost.View.ActualThemeVariantChanged += (_, _) => _chrome?.ApplyThemeVariant();
        OnShellReady(host);
        RefreshPanelSessions();
        RefreshPanelApplications();
        _view?.Post(shell.Publish);
        if (!_shellStarted)
        {
            _shellStarted = true;
            StartInitialWork(host);
            if (_run!.OpenSettings)
            {
                OpenSettings();
            }
        }
    }

    protected virtual void OnShellReady(BasinCompositorHost host)
    {
    }

    Task<string?> ISshPrompter.AskSecretAsync(SshSecretPrompt prompt, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        return AskAsync(SshPromptText.For(prompt), AskPassKind.Password, cancellation);
    }

    async Task<bool> ISshPrompter.ConfirmHostKeyAsync(SshHostKeyPrompt prompt, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        return await AskAsync(SshPromptText.For(prompt), AskPassKind.YesNo, cancellation) is "yes";
    }

    private Task<string?> AskAsync(string prompt, AskPassKind kind, CancellationToken cancellation)
    {
        var answered = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        cancellation.Register(() => answered.TrySetResult(null));
        Dispatcher.UIThread.Post(() =>
        {
            if (_shuttingDown)
            {
                answered.TrySetResult(null);
                return;
            }

            var asked = _chrome is { } chrome ? chrome.AskPass(prompt, kind) : AskOutsideShellAsync(prompt, kind, cancellation);
            _ = asked.ContinueWith(
                task => answered.TrySetResult(task.IsCompletedSuccessfully ? task.Result : null),
                TaskScheduler.Default);
        });
        return answered.Task;
    }

    protected virtual Task<string?> AskOutsideShellAsync(string prompt, AskPassKind kind, CancellationToken cancellation)
    {
        Log.Warn($"an ssh prompt arrived before the shell was up, canceling the login");
        return Task.FromResult<string?>(null);
    }

    private void PublishPanelModel()
    {
        if (_shell is not { } shell)
        {
            return;
        }

        var windows = shell.SnapshotWindows();
        var workspaces = shell.SnapshotWorkspaces();
        var current = shell.Workspaces.Current;
        var rows = shell.Workspaces.Rows;
        var workArea = shell.WorkArea;
        Dispatcher.UIThread.Post(() =>
        {
            if (_panelModel is not { } model)
            {
                return;
            }

            model.Windows = windows;
            model.Workspaces = workspaces;
            model.CurrentWorkspace = current;
            model.WorkspaceRows = rows;
            model.WorkArea = workArea;
            UpdateShellTitle();
        });
    }

    protected void RefreshPanelSessions()
    {
        if (_panelModel is not { } model || _registry is not { } registry)
        {
            return;
        }

        var sessions = new List<PanelSessionInfo>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var session in registry.Sessions)
        {
            seen.Add(session.Name);
            sessions.Add(new PanelSessionInfo(session.Name, session.Status));
        }

        foreach (var profile in _catalog.Profiles)
        {
            if (seen.Add(profile.Name))
            {
                sessions.Add(new PanelSessionInfo(profile.Name, SessionStatus.Disconnected));
            }
        }

        model.Sessions = sessions;
        UpdateShellTitle();
    }

    protected void RefreshPanelApplications()
    {
        if (_panelModel is not { } model || _registry is not { } registry)
        {
            return;
        }

        var entries = new List<SessionMenuEntry>();
        foreach (var session in registry.Sessions)
        {
            entries.Add(new SessionMenuEntry(
                session.Name,
                session.Status,
                session.Applications,
                session.ApplicationsNotice,
                () => ConnectByName(session.Name),
                () => _ = DisconnectSessionAsync(session.Name),
                () => _ = session.LoadApplicationsAsync()));
        }

        model.Applications = SessionMenu.Build(entries, LocalApplications, LocalNotice, null, null);
    }

    private void UpdateShellTitle()
    {
        if (_shellHost is not { } shellHost || _registry is not { } registry)
        {
            return;
        }

        shellHost.SetTitle(ShellTitle(registry));
    }

    protected virtual string ShellTitle(SessionRegistry registry)
    {
        var live = registry.Live.ToList();
        return live.Count == 1 ? $"Waylonia — {live[0].Name}" : "Waylonia";
    }

    protected void RefreshShellTitle() => UpdateShellTitle();

    protected void ShowShell() => _shellHost?.Present();

    private void ToggleShellFullScreen()
    {
        if (_shellHost is { CanFullScreen: true } shellHost)
        {
            shellHost.ToggleFullScreen();
        }
    }

    private void OnHostReady(BasinCompositorHost host)
    {
        BasinReport.Line(ReportLines.Socket(host.Socket));
        UpdateStatus(_run!.ManagedTransport ? "ready" : $"waiting for clients on {host.Socket}");
        OnHostReadyCore(host);
    }

    protected virtual void OnHostReadyCore(BasinCompositorHost host) => OpenShell(host);

    protected virtual void StartInitialWork(BasinCompositorHost host)
    {
        foreach (var settings in _run!.Initial)
        {
            _ = ConnectSessionAsync(settings);
        }
    }

    protected async Task<string?> ConnectSessionAsync(SessionSettings settings)
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

        if (settings.IsDesktop)
        {
            PrepareDesktopSession(settings);
        }

        var session = registry.Add(settings);
        return await session.ConnectAsync() ? null : session.LastError ?? $"{settings.Name} could not connect";
    }

    protected virtual void PrepareDesktopSession(SessionSettings settings)
    {
    }

    protected Task<string?> ConnectProfileAsync(SessionProfile profile)
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

    protected async Task DisconnectSessionAsync(string name)
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

        OnRegistryChanged();
        RefreshPanelSessions();
        RefreshPanelApplications();
    });

    protected virtual void OnRegistryChanged()
    {
    }

    private void OnSessionEnded(SshSession session, int code) => Dispatcher.UIThread.Post(() =>
    {
        if (_shuttingDown || _registry is not { } registry)
        {
            return;
        }

        if (_resumePlan.Contains(session.Name))
        {
            if (_suspended)
            {
                Log.Debug($"{session.Name} ended while the host was in the background; it reconnects on return");
                return;
            }

            _resumePlan.Remove(session.Name);
            if (_catalog.Find(session.Name) is { } profile)
            {
                Log.Info($"reconnecting {session.Name} after the host came back");
                _ = ConnectProfileAsync(profile);
                return;
            }
        }

        var othersLive = registry.Live.Any(other => !ReferenceEquals(other, session));
        if (SessionRegistry.ExitsProcess(session.Settings.AdHoc, session.CommandFinished, session.HadClients, othersLive, _run!.Manager))
        {
            _ = ShutdownAsync(code);
        }
    });

    private void RefreshCatalog()
    {
        _catalog = _run!.Store.Load(Log);
        OnCatalogChanged();
    }

    protected virtual void OnCatalogChanged()
    {
    }

    protected void OpenManager()
    {
        if (_shuttingDown || _registry is not { } registry)
        {
            return;
        }

        if (_managerModel is null)
        {
            var run = _run!;
            _managerModel = new ManagerViewModel(
                run.Store,
                registry,
                Log,
                ConnectProfileAsync,
                DisconnectSessionAsync,
                run.Config.Path is null ? null : OpenSettings,
                run.Capabilities.KeyImport ? PickKeysAsync : null,
                run.Paths.SshDirectory);
        }
        else
        {
            _managerModel.Reload();
        }

        if (_chrome is { } chrome)
        {
            chrome.OpenManager(_managerModel);
            return;
        }

        OpenManagerOutsideShell(_managerModel);
    }

    private Task<IReadOnlyList<PickedKey>> PickKeysAsync() => _platform!.Keys.PickAsync(_shellHost?.TopLevel);

    protected virtual void OpenManagerOutsideShell(ManagerViewModel model) =>
        Log.Warn($"the session manager needs the nested shell on this host");

    protected void OpenSettings()
    {
        if (_shuttingDown || _run!.Config.Path is not { } path)
        {
            return;
        }

        if (_settingsModel is null)
        {
            _settingsModel = new SettingsViewModel(path, _startup ?? _run.Config.Values, Log, ApplyConfig);
        }
        else
        {
            _settingsModel.Reload();
        }

        if (_chrome is { } chrome)
        {
            chrome.OpenSettings(_settingsModel);
            return;
        }

        OpenSettingsOutsideShell(_settingsModel);
    }

    protected virtual void OpenSettingsOutsideShell(SettingsViewModel model) =>
        Log.Warn($"the settings window needs the nested shell on this host");

    private void ApplyConfig(Config config)
    {
        if (_shuttingDown)
        {
            return;
        }

        _run = _run! with { Host = config.Host with { Shell = _run.Host.Shell }, Config = config };
        if (_shell is { } shell && _run.Agent is null)
        {
            var settings = config.ShellSettings;
            var panels = PanelArrangement.From(config.Panel, _run.Capabilities, Log);
            var keys = KeyTable.Build(settings.Keys, Hotkey.Reserved(config.Host.Hotkeys));
            var sessionTitles = config.Host.SessionTitles;
            var panelsChanged = _panelArrangement is null || !_panelArrangement.SameAs(panels);
            _panelArrangement = panels;
            _view?.Post(() =>
            {
                shell.WindowSuffix = sessionTitles ? SessionNameOf : null;
                shell.Apply(settings, panels.Layout, keys);
                if (panelsChanged)
                {
                    Dispatcher.UIThread.Post(() => _chrome?.RebuildPanels(panels));
                }
            });
        }

        if (_panelModel is { } model)
        {
            model.SettingsAvailable = config.Path is not null;
        }

        OnConfigApplied(config);
    }

    protected virtual void OnConfigApplied(Config config)
    {
    }

    protected string? SessionNameOf(WlClient client) =>
        _channelClients.OwnerOf(client) is WaypipeAcceptor { Session: not null } owner ? owner.Name : null;

    protected void Launch(string? session, string label, string command)
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

    protected void LaunchInSession(string name, string label, string command)
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

    protected virtual void LaunchLocal(string command, string label) =>
        Log.Warn($"{label}: '{command}' runs a local client, which this host cannot start");

    protected void ConnectByName(string name)
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

    protected virtual void Quit()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime)
        {
            _ = ShutdownAsync(0);
            return;
        }

        if (_registry is { } registry)
        {
            foreach (var session in registry.Live.ToList())
            {
                _ = session.DisconnectAsync();
            }
        }
    }

    protected virtual void UpdateStatus(string text)
    {
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
    protected static void Protocol(string line) => BasinReport.Line(line);

    protected async Task ShutdownAsync(int status)
    {
        if (_shuttingDown)
        {
            return;
        }

        _shuttingDown = true;
        _exitStatus = status;
        _platform!.Cursor.Close();
        await OnShuttingDownAsync();
        _managerModel?.Detach();

        var stopped = false;
        if (_registry is { } registry)
        {
            foreach (var session in registry.Sessions)
            {
                stopped |= session.StopClients();
            }
        }

        stopped |= StopLocalClients();
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
        _mixer.Dispose();
        if (_view is { } integrationPump && _clipboard is { } clipboard)
        {
            integrationPump.Post(clipboard.Dispose);
        }

        await CloseHostWindowsAsync();
        if (_shellHost is { } shellHost)
        {
            _chrome?.Dispose();
            _chrome = null;
            if (_shell is { } shell && _view is { } shellPump)
            {
                shellPump.Post(shell.Dispose);
            }

            await shellHost.CloseAsync();
            _shellHost = null;
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

        ExitProcess(_exitStatus);
    }

    protected virtual Task OnShuttingDownAsync() => Task.CompletedTask;

    protected virtual bool StopLocalClients() => false;

    protected virtual Task CloseHostWindowsAsync() => Task.CompletedTask;

    protected virtual void ExitProcess(int status)
    {
    }
}
