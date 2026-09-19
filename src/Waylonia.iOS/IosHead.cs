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
        var library = NSFileManager.DefaultManager.GetUrls(NSSearchPathDirectory.LibraryDirectory, NSSearchPathDomain.User) is { Length: > 0 } urls
            && urls[0].Path is { Length: > 0 } path
            ? path
            : Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))!;
        return WayloniaPaths.Sandbox(documents, library);
    }

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
        var library = Path.GetDirectoryName(Path.GetDirectoryName(paths.IconCacheRoot))!;
        Environment.SetEnvironmentVariable("XDG_STATE_HOME", Path.Combine(library, "Application Support"));
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", Path.Combine(library, "Application Support"));
        Environment.SetEnvironmentVariable("XDG_CACHE_HOME", Path.Combine(library, "Caches"));
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", Path.GetDirectoryName(paths.ConfigFile));
        try
        {
            Directory.CreateDirectory(paths.SshDirectory);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            log.Warn($"cannot create {paths.SshDirectory}: {error.Message}");
        }

        var config = Config.Load(paths, log);
        var store = new SessionStore(config.SessionsDirectory);
        var host = config.Host with { Shell = ShellMode.Nested, Tray = false };
        var initial = RunRules.Autoconnect(store.Load(log), [], config, "f32", log);
        return new WayloniaRun(
            Capabilities,
            paths,
            host,
            initial,
            Manager: true,
            LocalCommand: null,
            LocalDesktop: null,
            WaypipeListen: null,
            Frames: 0,
            Screenshot: null,
            SocketName: null,
            config,
            store);
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
