using Basin.Diagnostics;
using Foundation;
using Waylonia.Audio;
using Waylonia.Sessions;
using Waylonia.Shell;
using static Waylonia.WayloniaLog;

namespace Waylonia;

internal static class IosHead
{
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

    public static WayloniaPaths Paths()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return WayloniaPaths.Sandbox(documents, StateRoot(), CacheRoot());
    }

    private static string Library() =>
        NSFileManager.DefaultManager.GetUrls(NSSearchPathDirectory.LibraryDirectory, NSSearchPathDomain.User) is { Length: > 0 } urls
            && urls[0].Path is { Length: > 0 } path
            ? path
            : Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))!;

    private static string StateRoot() => Path.Combine(Library(), "Application Support");

    private static string CacheRoot() => Path.Combine(Library(), "Caches");

    public const string LogVariable = "WAYLONIA_LOG";

    public static IVideoDecoders Video { get; } = new IosVideoDecoders();

    public static WayloniaRun BuildRun()
    {
        SessionSettings.CreateDecoder = Video.Create;
        BasinLog.Level = Enum.TryParse<BasinLogLevel>(Environment.GetEnvironmentVariable(LogVariable), ignoreCase: true, out var level)
            ? level
            : BasinLogLevel.Info;
        BasinLog.Sink = new StandardErrorLogSink();
        var log = BasinLog.For("waylonia");
        var paths = Paths();
        MobileRun.ExportXdg(StateRoot(), CacheRoot(), Path.GetDirectoryName(paths.ConfigFile)!);
        return MobileRun.Build(paths, Capabilities, log);
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
        var sink = AVAudioSink.TryCreate(fill, rate, channels, out var whyNot);
        if (sink is null)
        {
            Log.Warn($"this device has no playback route, so no session has sound: {whyNot}");
        }

        return sink;
    }
}
