using Avalonia;
using Avalonia.Headless;

namespace Waylonia.Tests;

public sealed class WayloniaTestApp : Application
{
    public override void Initialize() => Styles.Add(new global::BluerCurve.BluerCurveTheme());

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<WayloniaTestApp>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
