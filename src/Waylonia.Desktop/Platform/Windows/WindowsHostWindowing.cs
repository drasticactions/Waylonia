using Avalonia;

namespace Waylonia;

internal sealed class WindowsHostWindowing : IHostWindowing
{
    public AppBuilder Configure(AppBuilder builder) => builder.UsePlatformDetect();
}
