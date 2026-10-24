using Avalonia;
using Avalonia.Headless;

namespace Waylonia.Tests;

public sealed class WayloniaTestApp : Application
{
    public override void Initialize() => WayloniaApp.AddTheme(Styles, new global::AvaUnion.UnionTheme { Engine = global::AvaUnion.Engine.UnionEngineKind.Managed });

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<WayloniaTestApp>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
