using Avalonia;
using Avalonia.Wayland;

namespace Waylonia;

internal sealed class LinuxHostWindowing : IHostWindowing
{
    public AppBuilder Configure(AppBuilder builder) => builder
        .UsePlatformDetect()
        .UseWaylandWithFallback();
}
