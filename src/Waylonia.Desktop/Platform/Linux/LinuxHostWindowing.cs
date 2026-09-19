using Avalonia;
using Avalonia.Wayland;

namespace Waylonia;

internal sealed class LinuxHostWindowing : IHostWindowing
{
#pragma warning disable AVALONIA_WAYLAND_FORCE_CSD
    public AppBuilder Configure(AppBuilder builder) => builder
        .UsePlatformDetect()
        .UseWaylandWithFallback()
        .With(new WaylandPlatformOptions { ForceDrawnDecorations = true });
#pragma warning restore AVALONIA_WAYLAND_FORCE_CSD
}
