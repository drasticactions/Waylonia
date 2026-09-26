using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Basin;
using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Hosted;
using Basin.Shell.Nested;
using Waylonia.Shell;
using static Waylonia.Agent.AgentLog;

namespace Waylonia.Agent;

[SupportedOSPlatform("linux")]
internal sealed class AgentHeadlessHost : IAgentCompositor, IDisposable
{
    private readonly AgentRuntime _runtime;
    private readonly ShellSettings _settings;
    private readonly string? _socketName;
    private readonly AgentXWayland? _xwayland;
    private readonly ManualResetEventSlim _quit = new();
    private readonly List<TaskCompletionSource<bool>> _frameWaiters = [];
    private readonly Lock _frameLock = new();
    private BasinHeadlessDriver? _driver;
    private IProtocolModule? _xwaylandModule;
    private BasinViewOutput? _view;
    private NestedShell? _shell;
    private bool _disposed;

    public AgentHeadlessHost(AgentRuntime runtime, ShellSettings configured, string? socketName, AgentXWayland? xwayland)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(configured);
        _runtime = runtime;
        _settings = AgentShell.Settings(runtime.Profile, configured);
        _socketName = socketName;
        _xwayland = xwayland;
    }

    public bool Headless => true;

    public event Action? Damaged;

    public NestedShell? Shell => _shell;

    public BasinCompositorHost? Host => _driver?.Host;

    public void Start()
    {
        var profile = _runtime.Profile;
        _driver = new BasinHeadlessDriver(() =>
        {
            _xwaylandModule = _xwayland?.Create();
            var host = new BasinCompositorHost(new BasinCompositorOptions
            {
                AppName = "waylonia",
                SocketName = _socketName,
                ManagedTransport = false,
                ExtraModules = _xwaylandModule is { } module ? [module] : null,
                ConfigureServices = _runtime.ConfigureServices,
                Dmabuf = false,
            });
            host.Seat.Keyboard.SetKeymap(new KeymapNames(Layout: profile.Keymap ?? "us"));
            var width = (int)Math.Round(profile.Width * profile.Scale);
            var height = (int)Math.Round(profile.Height * profile.Scale);
            _view = host.CreateViewOutput(width, height, profile.Scale, NestedShell.OutputKey);
            _shell = new NestedShell(
                host, _view, _settings, AgentShell.Panels, KeyTable.Empty, Post, [BundledThemes.Load]);
            host.Session.BeforeDispatch += _ => _shell.Tick(Environment.TickCount64);
            if (_xwaylandModule is { } attach)
            {
                _xwayland!.AttachShell(attach, _shell);
                _runtime.XDisplay = () => _xwayland.DisplayName(host);
            }

            _runtime.Attach(host, _view, _shell, this, RequestQuit);
            BasinReport.Line(Cli.ReportLines.Socket(host.Socket));
            return host;
        });
        _driver.FrameCompleted += OnFrame;
        _driver.Start();
    }

    public int Run()
    {
        using var interrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, OnSignal);
        using var terminate = PosixSignalRegistration.Create(PosixSignal.SIGTERM, OnSignal);
        using var hangup = PosixSignalRegistration.Create(PosixSignal.SIGHUP, OnSignal);
        try
        {
            Start();
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            Log.Error($"the headless compositor could not start: {failure.Message}");
            return 1;
        }

        _quit.Wait();
        Stop();
        return 0;
    }

    public void RequestQuit() => _quit.Set();

    public Task<T> RunAsync<T>(Func<NestedShell, T> work, CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (_driver is not { } driver)
        {
            return Task.FromException<T>(new InvalidOperationException("the headless compositor is not running"));
        }

        return driver.InvokeAsync(() => work(_shell!)).WaitAsync(cancel);
    }

    public Task<bool> NextFrameAsync(CancellationToken cancel)
    {
        var waiter = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_frameLock)
        {
            _frameWaiters.Add(waiter);
        }

        _driver?.RequestFrame();
        return waiter.Task.WaitAsync(cancel);
    }

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (_driver is { IsDriverThread: true })
        {
            action();
            return;
        }

        _driver?.Post(action);
    }

    public void Stop()
    {
        if (_driver is not { } driver)
        {
            return;
        }

        driver.Stop(() =>
        {
            _runtime.Detach();
            _shell?.Dispose();
            _shell = null;
            _view?.Dispose();
            _view = null;
        });
        _driver = null;
        lock (_frameLock)
        {
            foreach (var waiter in _frameWaiters)
            {
                waiter.TrySetResult(false);
            }

            _frameWaiters.Clear();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
        _quit.Dispose();
    }

    private void OnSignal(PosixSignalContext context)
    {
        context.Cancel = true;
        RequestQuit();
    }

    private void OnFrame()
    {
        TaskCompletionSource<bool>[] waiters;
        lock (_frameLock)
        {
            waiters = [.. _frameWaiters];
            _frameWaiters.Clear();
        }

        foreach (var waiter in waiters)
        {
            waiter.TrySetResult(true);
        }

        Damaged?.Invoke();
    }
}
