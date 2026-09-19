using Avalonia;
using Avalonia.iOS;
using Foundation;

namespace Waylonia;

[Register("AppDelegate")]
internal sealed class AppDelegate : AvaloniaAppDelegate<WayloniaApp>
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) =>
        base.CustomizeAppBuilder(builder)
            .With(new iOSPlatformOptions
            {
                RenderingMode = [iOSRenderingMode.Metal, iOSRenderingMode.OpenGl],
            });
}
