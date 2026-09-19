using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Avalonia.Android;
using static Waylonia.WayloniaLog;

namespace Waylonia;

[Activity(
    Name = "dev.drasticactions.waylonia.MainActivity",
    Label = "Waylonia",
    Theme = "@style/Theme.Waylonia",
    MainLauncher = true,
    Exported = true,
    ResizeableActivity = true,
    LaunchMode = LaunchMode.SingleTask,
    WindowSoftInputMode = SoftInput.AdjustResize,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.SmallestScreenSize
        | ConfigChanges.ScreenLayout | ConfigChanges.Density | ConfigChanges.UiMode
        | ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden | ConfigChanges.Navigation)]
internal sealed class MainActivity : AvaloniaMainActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        BackRequested += (_, e) =>
        {
            e.Handled = true;
            (Avalonia.Application.Current as WayloniaApp)?.PressKey(AndroidHead.EscapeKey);
        };
        if (OperatingSystem.IsAndroidVersionAtLeast(36) && CheckSelfPermission(AndroidHead.LocalNetworkPermission) != Permission.Granted)
        {
            RequestPermissions([AndroidHead.LocalNetworkPermission], 0);
        }

        Log.Debug($"the activity was created");
    }

    protected override void OnStart()
    {
        base.OnStart();
        Log.Debug($"the activity started");
    }

    protected override void OnStop()
    {
        Log.Debug($"the activity stopped");
        base.OnStop();
    }

    protected override void OnDestroy()
    {
        Log.Debug($"the activity was destroyed; the shell view outlives it");
        base.OnDestroy();
    }
}
