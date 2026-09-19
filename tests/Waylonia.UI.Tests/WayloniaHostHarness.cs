using Avalonia.Threading;
using Basin.Avalonia;
using Basin.Hosted;
using Basin.Shell.Xdg.Protocol;
using Wayland;
using Xunit;

namespace Waylonia.Tests;

internal sealed class WayloniaHostHarness : CompositorHarness
{
    private ZwlrLayerShellV1? _layerShell;
    private ToplevelWindows? _windows;

    public WayloniaHostHarness(
        Action<BasinCompositorHost>? configure = null,
        Action<Wayland.Server.WlClient>? onClient = null,
        NestedShellOptions? nested = null)
        : base(configure, onClient, nested)
    {
    }

    public ToplevelWindows? Windows => _windows;

    protected override IDisposable? AttachWindows(BasinCompositorHost host) =>
        _windows = new ToplevelWindows(host, action => action(), requestFrame: () => FrameRequests++);

    protected override int HostWindowCount => Windows!.Windows.Count;

    public override void Pump()
    {
        base.Pump();
        Dispatcher.UIThread.RunJobs();
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

    protected override void AfterClientsClosed() => Dispatcher.UIThread.RunJobs();

    protected override void AfterHostDisposed() => Dispatcher.UIThread.RunJobs();
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
