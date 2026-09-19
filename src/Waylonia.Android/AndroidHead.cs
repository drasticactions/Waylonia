using System.Diagnostics;
using Android.Content;
using Basin.Diagnostics;
using Waylonia.Audio;
using Waylonia.Sessions;
using static Waylonia.WayloniaLog;

namespace Waylonia;

internal static class AndroidHead
{
    public const uint EscapeKey = 1;

    public const string LogVariable = "WAYLONIA_LOG";

    public const string LogProperty = "debug.waylonia.log";

    public const string LocalNetworkPermission = "android.permission.ACCESS_LOCAL_NETWORK";

    public static HostCapabilities Capabilities { get; } = new(
        Tray: false,
        GlobalHotkeys: false,
        KeyboardCapture: false,
        LocalCommands: false,
        LocalDesktops: false,
        XWayland: false,
        LocalApplications: false,
        ChannelsOnly: true,
        SoftKeyboard: true,
        KeyImport: true,
        Reconnects: true);

    public static IVideoDecoders Video { get; } = new AndroidVideoDecoders();

    public static WayloniaPaths Paths(Context context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var (documents, stateRoot, cacheRoot) = Roots(context);
        return WayloniaPaths.Sandbox(documents, stateRoot, cacheRoot);
    }

    private static (string Documents, string StateRoot, string CacheRoot) Roots(Context context)
    {
        var stateRoot = context.FilesDir!.AbsolutePath;
        var cacheRoot = context.CacheDir!.AbsolutePath;
        var external = context.GetExternalFilesDir(null);
        var documents = external is not null
            && Android.OS.Environment.GetExternalStorageState(external) == Android.OS.Environment.MediaMounted
            ? external.AbsolutePath
            : stateRoot;
        return (documents, stateRoot, cacheRoot);
    }

    public static WayloniaRun BuildRun(Context context)
    {
        ArgumentNullException.ThrowIfNull(context);
        SessionSettings.CreateDecoder = Video.Create;
        BasinLog.Level = LogLevel();
        BasinLog.Sink = new LogcatLogSink();
        var log = BasinLog.For("waylonia");
        var (_, stateRoot, cacheRoot) = Roots(context);
        var paths = Paths(context);
        MobileRun.ExportXdg(stateRoot, cacheRoot, Path.GetDirectoryName(paths.ConfigFile)!);
        return MobileRun.Build(paths, Capabilities, log);
    }

    private static BasinLogLevel LogLevel()
    {
        if (TryParse(Environment.GetEnvironmentVariable(LogVariable), out var fromEnvironment))
        {
            return fromEnvironment;
        }

        return TryParse(ReadProperty(LogProperty), out var fromProperty) ? fromProperty : BasinLogLevel.Info;
    }

    private static bool TryParse(string? text, out BasinLogLevel level) =>
        Enum.TryParse(text?.Trim(), ignoreCase: true, out level) && level != BasinLogLevel.None;

    private static string? ReadProperty(string name)
    {
        try
        {
            using var getprop = Process.Start(new ProcessStartInfo("/system/bin/getprop", name)
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
            });
            if (getprop is null)
            {
                return null;
            }

            var value = getprop.StandardOutput.ReadToEnd();
            getprop.WaitForExit();
            return value;
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
    }

    public static HostPlatform Compose(WayloniaRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return new HostPlatform(
            run.Capabilities,
            run.Paths,
            new TmdsSshLinkFactory(() => TimeSpan.FromSeconds(run.Host.SshTimeout), Log, run.Paths, enumerateKeys: true),
            OpenAudio,
            Video,
            NullHostCursor.Instance,
            NullHostScreenScales.Instance,
            NullHostWindowing.Instance,
            new KeyImporter());
    }

    private static IAudioSink? OpenAudio(AudioFill fill, int rate, int channels)
    {
        var sink = AudioTrackSink.TryCreate(fill, rate, channels, out var whyNot);
        if (sink is null)
        {
            Log.Warn($"this device has no playback route, so no session has sound: {whyNot}");
        }

        return sink;
    }
}
