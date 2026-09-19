using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;

namespace Waylonia;

[Application]
internal sealed class WayloniaApplication : AvaloniaAndroidApplication<WayloniaApp>
{
    public WayloniaApplication(nint javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }

    public override void OnCreate()
    {
        var run = AndroidHead.BuildRun(this);
        WayloniaApp.Prepare(run, AndroidHead.Compose(run));
        base.OnCreate();
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) =>
        base.CustomizeAppBuilder(builder)
            .With(new AndroidPlatformOptions
            {
                RenderingMode = [AndroidRenderingMode.Egl, AndroidRenderingMode.Software],
            });
}
