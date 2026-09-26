using System.Runtime.Versioning;
using Basin;
using Basin.Diagnostics;
using Basin.Freedesktop;
using Basin.Hosted;
using Basin.Ipc;
using Basin.Scene;
using Basin.Shell.Nested;
using static Waylonia.Agent.AgentLog;

namespace Waylonia.Agent;

[SupportedOSPlatform("linux")]
internal sealed class AgentRuntime : IDisposable
{
    public const string ScreencopyGlobal = "zwlr_screencopy_manager_v1";

    public const string ImageCopyGlobal = "ext_image_copy_capture_manager_v1";

    public const int ApprovalTimeoutSeconds = 120;

    public static IReadOnlyList<string> Omitted { get; } = ["process/spawn", "seat/*"];

    private readonly DateTimeOffset _started = DateTimeOffset.UtcNow;
    private IDisposable? _lock;
    private AgentRuntimeDirectory? _runtime;
    private AgentSessionBus? _bus;
    private SceneCapturePack? _capture;
    private DesktopEntries? _desktopEntries;
    private IEventSource? _takeoverTimer;
    private int _launches;
    private bool _detached;
    private bool _disposed;

    public AgentRuntime(AgentProfile profile, bool headless)
    {
        ArgumentNullException.ThrowIfNull(profile);
        Profile = profile;
        Headless = headless;
        Takeover = new AgentTakeover(headless ? 0 : profile.TakeoverTimeout);
        Approvals = new IpcApprovalBroker(TimeSpan.FromSeconds(ApprovalTimeoutSeconds));
        Audit = new AgentAudit(profile.Audit, _started, profile.AuditText);
        Interceptor = new AgentInterceptor(profile, Takeover, Approvals, Audit, PlanFromJson);
    }

    public AgentProfile Profile { get; }

    public bool Headless { get; }

    public AgentTakeover Takeover { get; }

    public IpcApprovalBroker Approvals { get; }

    public AgentAudit Audit { get; }

    public AgentInterceptor Interceptor { get; }

    public IpcServer? Server { get; private set; }

    public NestedShell? Shell { get; private set; }

    public BasinCompositorHost? Host { get; private set; }

    public BasinViewOutput? View { get; private set; }

    public IAgentCompositor? Compositor { get; private set; }

    public Func<string?>? XDisplay { get; set; }

    public string? SocketPath { get; init; }

    public string? BusAddress => _bus?.Address;

    public string? BusProblem { get; private set; }

    public string RuntimeDirectory => _runtime?.Path ?? string.Empty;

    public string WaylandSocket => _runtime?.WaylandSocket ?? string.Empty;

    public AgentAccessibility? Accessibility { get; private set; }

    public string AccessibilityStatus => Accessibility is { } a11y
        ? a11y.Status
        : !Profile.Accessibility
            ? "off: [accessibility] enabled = false in agent.toml"
            : $"off: {BusProblem ?? "the accessibility bus did not start"}";

    public bool Prepare(out string? error)
    {
        _lock = AgentProfileStore.Lock(Profile, out error);
        if (_lock is null)
        {
            return false;
        }

        _runtime = AgentRuntimeDirectory.Create(Profile.Name, out error);
        if (_runtime is null)
        {
            return false;
        }

        var environment = AgentEnvironment.Build(Profile, _runtime.Path, display: null, busAddress: null)
            .Apply(AgentEnvironment.Inherited());
        _bus = AgentSessionBus.Start(environment, _runtime.Path, Path.Combine(Profile.Logs, "dbus-daemon.log"), out var busError);
        if (_bus is null)
        {
            BusProblem = busError;
            Log.Warn($"{busError}");
        }
        else if (Profile.Accessibility)
        {
            if (AgentAccessibility.Unavailable() is { } why)
            {
                BusProblem = why;
                Log.Warn($"{why}");
            }
            else
            {
                Accessibility = new AgentAccessibility(
                    _bus.Address,
                    AgentEnvironment.Build(Profile, _runtime.Path, display: null, busAddress: _bus.Address).Apply(AgentEnvironment.Inherited()));
            }
        }

        Interceptor.Note(
            "run-started",
            ("profile", Profile.Name),
            ("mode", Headless ? "headless" : "visible"),
            ("pid", System.Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        error = null;
        return true;
    }

    public void ConfigureServices(BasinCompositorHost host, BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(services);
        VirtualInputGlobals.Apply(services, keep: false);
        services.Without(ScreencopyGlobal).Without(ImageCopyGlobal);
        services.With(_capture = new SceneCapturePack(host.Scene, host.Layout));
    }

    public void Attach(BasinCompositorHost host, BasinViewOutput view, NestedShell shell, IAgentCompositor compositor, Action quit)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(compositor);
        ArgumentNullException.ThrowIfNull(quit);
        Host = host;
        View = view;
        Shell = shell;
        Compositor = compositor;
        if (host.Socket.Length > 0)
        {
            _runtime?.LinkWayland(host.Socket);
        }

        if (_capture is { } capture)
        {
            shell.AttachCapture(capture);
        }

        var server = new IpcServer(host.Loop, host.Services, new IpcSessionInfo
        {
            Compositor = "waylonia",
            Backend = Headless ? "headless" : "hosted",
            WaylandSocket = WaylandSocket.Length > 0 ? WaylandSocket : host.Socket,
            XwaylandDisplay = () => XDisplay?.Invoke(),
            Quit = quit,
        }, socketPath: SocketPath)
        {
            SyntheticInput = new NestedSyntheticInput(shell),
            Interceptor = Interceptor,
            Approvals = Approvals,
        };
        server.Omit([.. Omitted]);
        Interceptor.Server = server;
        Interceptor.Internal = new IpcClientState();
        AgentMethods.Register(server, this);
        if (Accessibility is { } a11y)
        {
            AgentA11yMethods.Register(server, this, a11y);
        }

        server.Start();
        Server = server;
        if (_capture is { } pack)
        {
            Interceptor.Shots = new AgentShots(pack.Capture, view.Output, host.Loop, shell.Toplevels, Audit);
        }

        Takeover.Changed += OnTakeoverChanged;
        BasinReport.Line($"AGENT {Profile.Name} {(Headless ? "headless" : "visible")} {WaylandSocket}");
    }

    public AgentEnvironment LaunchEnvironment() =>
        AgentEnvironment.Build(Profile, RuntimeDirectory, XDisplay?.Invoke(), _bus?.Address);

    public DesktopEntry? FindDesktop(string id) =>
        (_desktopEntries ??= new DesktopEntries(DesktopLocale.FromEnvironment())).Find(id);

    public IReadOnlyList<DesktopEntry> AllDesktop() =>
        (_desktopEntries ??= new DesktopEntries(DesktopLocale.FromEnvironment())).All();

    public string NextLogPath(string name)
    {
        var safe = new string(name.Select(static c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' ? c : '_').ToArray());
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        return Path.Combine(Profile.Logs, $"{safe}-{stamp}-{Interlocked.Increment(ref _launches)}.log");
    }

    public void HostInput(bool pressOrKey)
    {
        if (Takeover.HostInput(System.Environment.TickCount64, pressOrKey))
        {
            Interceptor.Note("takeover", ("state", "paused"));
        }
    }

    public void Resume()
    {
        if (Takeover.Resume())
        {
            Interceptor.Note("takeover", ("state", "driving"), ("by", "button"));
        }
    }

    public void Detach()
    {
        if (_detached)
        {
            return;
        }

        _detached = true;
        Takeover.Changed -= OnTakeoverChanged;
        _takeoverTimer?.Remove();
        _takeoverTimer = null;
        Interceptor.Shots?.Flush();
        if (Server is { } server)
        {
            var running = server.Processes.Processes.Count(static process => process.IsRunning);
            server.Processes.TerminateAll(TimeSpan.FromSeconds(2));
            Interceptor.Note("run-ending", ("terminated", running.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            server.Dispose();
            Server = null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Accessibility?.Dispose();
        _bus?.Dispose();
        _runtime?.Dispose();
        Audit.Dispose();
        _lock?.Dispose();
        try
        {
            File.Delete(Profile.LockFile);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            Log.Debug($"the profile lock {Profile.LockFile} stays: {failure.Message}");
        }
    }

    private void OnTakeoverChanged()
    {
        if (Host is not { } host)
        {
            return;
        }

        _takeoverTimer?.Remove();
        _takeoverTimer = null;
        if (Takeover.ResumesAt is not { } at)
        {
            return;
        }

        _takeoverTimer = host.Loop.AddTimer(() =>
        {
            _takeoverTimer?.Remove();
            _takeoverTimer = null;
            if (Takeover.Tick(System.Environment.TickCount64))
            {
                Interceptor.Note("takeover", ("state", "driving"), ("by", "timeout"));
            }
            else
            {
                OnTakeoverChanged();
            }
        });
        _takeoverTimer.UpdateTimer((int)Math.Clamp(at - System.Environment.TickCount64, 1, int.MaxValue));
    }

    public AgentApprovalPrompt PromptFor(IpcApproval approval)
    {
        ArgumentNullException.ThrowIfNull(approval);
        var arguments = System.Text.Encoding.UTF8.GetString(approval.Arguments.Span);
        var timeout = Approvals.Timeout;
        switch (approval.Method)
        {
            case "waylonia/launch" when PlanFromJson(approval.Arguments.Span) is { } plan:
                return AgentApprovalText.For(
                    approval.Method, arguments, timeout, commandLine: string.Join(' ', plan.Argv), commandKey: AgentLauncher.Key(plan));
            case "windows/close" when Number(arguments, "id") is { } id && Shell is { } shell:
                foreach (var window in shell.Windows)
                {
                    if (window.IsMapped && shell.ToplevelIdOf(window) == id)
                    {
                        return AgentApprovalText.For(
                            approval.Method, arguments, timeout, window: AgentApprovalText.Window(window.Title, window.Content.AppId));
                    }
                }

                break;
            case "process/kill" when Number(arguments, "launch_id") is { } launch && Server?.Processes.Find((long)launch) is { } process:
                return AgentApprovalText.For(
                    approval.Method, arguments, timeout,
                    launch: $"{string.Join(' ', process.Argv)}  (launch {process.LaunchId}, pid {process.Pid})");
        }

        return AgentApprovalText.For(approval.Method, arguments, timeout);
    }

    private static ulong? Number(string json, string name)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty(name, out var value))
            {
                return null;
            }

            return value.ValueKind switch
            {
                System.Text.Json.JsonValueKind.Number when value.TryGetUInt64(out var number) => number,
                System.Text.Json.JsonValueKind.String when ulong.TryParse(value.GetString(), out var digits) => digits,
                _ => null,
            };
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private AgentLaunchPlan? PlanFromJson(ReadOnlySpan<byte> parameters)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(parameters.IsEmpty ? "{}"u8.ToArray() : parameters.ToArray());
            var root = document.RootElement;
            string? command = root.TryGetProperty("command", out var c) && c.ValueKind == System.Text.Json.JsonValueKind.String ? c.GetString() : null;
            string? desktop = root.TryGetProperty("desktop_id", out var d) && d.ValueKind == System.Text.Json.JsonValueKind.String ? d.GetString() : null;
            var args = new List<string>();
            if (root.TryGetProperty("args", out var a) && a.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var item in a.EnumerateArray())
                {
                    if (item.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        args.Add(item.GetString()!);
                    }
                }
            }

            return AgentLauncher.Plan(Profile.Allow, command, args, desktop, FindDesktop, out _);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
