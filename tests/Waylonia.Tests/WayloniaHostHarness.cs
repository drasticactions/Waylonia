using System.Runtime.InteropServices;
using Avalonia.Threading;
using Basin.Avalonia;
using Basin.Diagnostics;
using Basin.Shell.Xdg.Protocol;
using Wayland;
using Waylonia.Shell;
using Xunit;

namespace Waylonia.Tests;

internal sealed class WayloniaHostHarness : IDisposable
{
    private const int AfUnix = 1;
    private const int SockStream = 1;

    [DllImport("libc", SetLastError = true)]
    private static extern unsafe int socketpair(int domain, int type, int protocol, int* fds);

    [DllImport("libc")]
    private static extern unsafe int poll(PollFd* fds, nuint count, int timeoutMs);

    [StructLayout(LayoutKind.Sequential)]
    private struct PollFd
    {
        public int Fd;
        public short Events;
        public short REvents;
    }

    public static bool HasWaylandClient { get; } =
        OperatingSystem.IsLinux() &&
        (NativeLibrary.TryLoad("wayland-client", out _) ||
            NativeLibrary.TryLoad("libwayland-client.so.0", out _));

    public static void SkipWithoutWaylandClient() =>
        Assert.SkipWhen(
            !HasWaylandClient,
            "this host has no libwayland client, and the host-window tests drive the compositor with one");

    private readonly List<ShmTestClient> _extraClients = [];

    public WayloniaHostHarness(
        Action<BasinCompositorHost>? configure = null,
        Action<Wayland.Server.WlClient>? onClient = null,
        NestedShellOptions? nested = null)
    {
        SkipWithoutWaylandClient();
        BasinCounters.Reset();
        Host = new BasinCompositorHost(new BasinCompositorOptions { AppName = "waylonia-tests" });
        if (nested is { } options)
        {
            ShellView = Host.CreateViewOutput(options.Width, options.Height, options.Scale, NestedShell.OutputKey);
            Shell = new NestedShell(
                Host,
                ShellView,
                options.Settings,
                PanelLayout.From(options.Panel, BasinLogger.None),
                KeyTable.Build(options.Settings.Keys, [], BasinLogger.None),
                _ => null,
                sessionTitles: true,
                action => action(),
                action => action(),
                BasinLog.For("waylonia-tests"));
        }
        else
        {
            Windows = new ToplevelWindows(Host, action => action(), requestFrame: () => FrameRequests++);
        }

        configure?.Invoke(Host);
        _client = Connect(onClient, client => _client = client);
    }

    public sealed record NestedShellOptions(
        int Width = 800,
        int Height = 600,
        double Scale = 1.0,
        ShellSettings? Settings = null,
        PanelSettings? Panel = null)
    {
        public ShellSettings Settings { get; init; } = Settings ?? new ShellSettings();

        public PanelSettings Panel { get; init; } = Panel ?? new PanelSettings();
    }

    public NestedShell? Shell { get; }

    public BasinViewOutput? ShellView { get; }

    public int WindowCount => Shell?.Windows.Count ?? Windows!.Windows.Count;

    private ShmTestClient Connect(Action<Wayland.Server.WlClient>? onClient, Action<ShmTestClient> register)
    {
        int serverFd, clientFd;
        unsafe
        {
            var fds = stackalloc int[2];
            Assert.Equal(0, socketpair(AfUnix, SockStream, 0, fds));
            serverFd = fds[0];
            clientFd = fds[1];
        }

        var server = Host.Display.CreateClient(serverFd);
        onClient?.Invoke(server);
        var client = new ShmTestClient(clientFd);
        register(client);
        client.BindGlobals(Pump);
        return client;
    }

    public ShmTestClient AddClient(Action<Wayland.Server.WlClient>? onClient = null) =>
        Connect(onClient, _extraClients.Add);

    private IEnumerable<ShmTestClient> Clients()
    {
        if (_client is { } client)
        {
            yield return client;
        }

        foreach (var extra in _extraClients)
        {
            yield return extra;
        }
    }

    private ZwlrLayerShellV1? _layerShell;

    public BasinCompositorHost Host { get; }

    public ToplevelWindows? Windows { get; }

    private ShmTestClient? _client;

    public ShmTestClient Client => _client!;

    public int FrameRequests { get; private set; }

    public void Pump()
    {
        foreach (var client in Clients())
        {
            client.Display.Flush();
        }

        Host.Loop.Dispatch(0);
        Host.Display.FlushClients();
        foreach (var client in Clients())
        {
            while (Readable(client))
            {
                client.Display.Dispatch();
            }

            client.Display.DispatchPending();
        }

        Dispatcher.UIThread.RunJobs();
    }

    public void PumpInput()
    {
        Pump();
        Host.Session.BeginFrame();
        Host.Session.EndFrame();
        Pump();
    }

    public void PumpUntil(Func<bool> settled, string what)
    {
        for (var i = 0; i < 200 && !settled(); i++)
        {
            Pump();
        }

        Assert.True(settled(), what);
    }

    public HarnessToplevel MapToplevel(
        int width = 120,
        int height = 90,
        string title = "waylonia",
        string appId = "waylonia.test",
        bool serverDecorated = false)
    {
        var existing = WindowCount;
        var surface = Client.Compositor.CreateSurface();
        var xdgSurface = Client.WmBase!.GetXdgSurface(surface);
        var toplevel = xdgSurface.GetToplevel();
        toplevel.SetTitle(title);
        toplevel.SetAppId(appId);

        var mapped = new HarnessToplevel(surface, xdgSurface, toplevel);
        xdgSurface.Configure += (_, e) =>
        {
            xdgSurface.AckConfigure(e.Serial);
            mapped.Configured = true;
        };
        toplevel.Configure += (_, e) =>
        {
            mapped.ConfiguredWidth = e.Width;
            mapped.ConfiguredHeight = e.Height;
        };
        toplevel.Close += (_, _) => mapped.CloseReceived = true;

        surface.Commit();
        PumpUntil(() => mapped.Configured, "the compositor never configured the toplevel");
        if (serverDecorated && Client.DecorationManager is { } decorations)
        {
            var decoration = decorations.GetToplevelDecoration(toplevel);
            mapped.Decoration = decoration;
            var configuredMode = false;
            decoration.Configure += (_, _) => configuredMode = true;
            decoration.SetMode(ZxdgToplevelDecorationV1.Mode.ServerSide);
            PumpUntil(() => configuredMode, "the compositor never answered the decoration mode");
        }

        var buffer = Client.CreateBuffer(width, height, Fill(width, height, 0xFF3366AA));
        mapped.Buffer = buffer;
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, width, height);
        surface.Commit();
        PumpUntil(() => WindowCount > existing, "the mapped toplevel never became a managed window");
        return mapped;
    }

    public HarnessLayer MapLayer(
        int width = 200,
        int height = 40,
        ZwlrLayerShellV1.Layer layer = ZwlrLayerShellV1.Layer.Top,
        string scope = "panel",
        ZwlrLayerSurfaceV1.Anchor anchor = 0,
        ZwlrLayerSurfaceV1.KeyboardInteractivity keyboard = ZwlrLayerSurfaceV1.KeyboardInteractivity.None)
    {
        var existing = Windows!.LayerWindows.Count;
        _layerShell ??= Client.BindAt<ZwlrLayerShellV1>("zwlr_layer_shell_v1", 4);
        var surface = Client.Compositor.CreateSurface();
        var layerSurface = _layerShell.GetLayerSurface(surface, null, layer, scope);
        layerSurface.SetSize((uint)width, (uint)height);
        layerSurface.SetAnchor(anchor);
        layerSurface.SetKeyboardInteractivity(keyboard);

        var mapped = new HarnessLayer(surface, layerSurface);
        layerSurface.Configure += (_, e) =>
        {
            layerSurface.AckConfigure(e.Serial);
            mapped.Configured = true;
        };
        layerSurface.Closed += (_, _) => mapped.CloseReceived = true;

        mapped.Buffer = Client.CreateBuffer(width, height, Fill(width, height, 0xFF22AA55));
        ShowLayer(mapped);
        PumpUntil(
            () => Windows!.LayerWindows.Count > existing,
            "the mapped layer surface never became a host window");
        return mapped;
    }

    public void ShowLayer(HarnessLayer mapped)
    {
        mapped.Configured = false;
        mapped.Surface.Commit();
        PumpUntil(() => mapped.Configured, "the compositor never configured the layer surface");

        var buffer = mapped.Buffer!;
        mapped.Surface.Attach(buffer.Proxy, 0, 0);
        mapped.Surface.Damage(0, 0, buffer.Width, buffer.Height);
        mapped.Surface.Commit();
    }

    public void SetInputRegion(HarnessLayer mapped, params (int X, int Y, int Width, int Height)[] rects)
    {
        var region = Client.Compositor.CreateRegion();
        foreach (var rect in rects)
        {
            region.Add(rect.X, rect.Y, rect.Width, rect.Height);
        }

        mapped.Surface.SetInputRegion(region);
        mapped.Surface.Commit();
        region.Destroy();
        Pump();
    }

    public void HideLayer(HarnessLayer mapped)
    {
        mapped.Surface.Attach(null, 0, 0);
        mapped.Surface.Commit();
    }

    public static Action<nint, int> Fill(int width, int height, uint color) => (data, stride) =>
    {
        unsafe
        {
            for (var y = 0; y < height; y++)
            {
                var row = (uint*)((byte*)data + (y * stride));
                for (var x = 0; x < width; x++)
                {
                    row[x] = color;
                }
            }
        }
    };

    private static bool Readable(ShmTestClient client)
    {
        unsafe
        {
            var pollFd = new PollFd { Fd = client.Display.Fd, Events = 1 };
            return poll(&pollFd, 1, 0) > 0 && (pollFd.REvents & 1) != 0;
        }
    }

    public void Dispose()
    {
        foreach (var extra in _extraClients)
        {
            extra.Dispose();
        }

        Client.Dispose();
        Host.Loop.Dispatch(0);
        Host.Loop.Dispatch(0);
        Dispatcher.UIThread.RunJobs();
        Windows?.Dispose();
        Shell?.Dispose();
        ShellView?.Dispose();
        Host.Dispose();
        Dispatcher.UIThread.RunJobs();
    }
}

internal sealed class HarnessToplevel(WlSurface surface, XdgSurface xdgSurface, XdgToplevel toplevel)
{
    public WlSurface Surface { get; } = surface;

    public XdgSurface XdgSurface { get; } = xdgSurface;

    public XdgToplevel Toplevel { get; } = toplevel;

    public ClientShmBuffer? Buffer { get; set; }

    public ZxdgToplevelDecorationV1? Decoration { get; set; }

    public bool Configured { get; set; }

    public bool CloseReceived { get; set; }

    public int ConfiguredWidth { get; set; }

    public int ConfiguredHeight { get; set; }

    public void Destroy()
    {
        Decoration?.Dispose();
        Toplevel.Dispose();
        XdgSurface.Dispose();
        Surface.Dispose();
    }
}

internal sealed class HarnessLayer(WlSurface surface, ZwlrLayerSurfaceV1 layerSurface)
{
    public WlSurface Surface { get; } = surface;

    public ZwlrLayerSurfaceV1 LayerSurface { get; } = layerSurface;

    public ClientShmBuffer? Buffer { get; set; }

    public bool Configured { get; set; }

    public bool CloseReceived { get; set; }

    public void Destroy()
    {
        LayerSurface.Dispose();
        Surface.Dispose();
    }
}
