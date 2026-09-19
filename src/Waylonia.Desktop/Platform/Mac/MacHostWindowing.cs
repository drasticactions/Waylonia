using Avalonia;

namespace Waylonia;

internal sealed class MacHostWindowing : IHostWindowing
{
    public AppBuilder Configure(AppBuilder builder) => builder
        .UsePlatformDetect()
        .With(new MacOSPlatformOptions { ShowInDock = false });
}
