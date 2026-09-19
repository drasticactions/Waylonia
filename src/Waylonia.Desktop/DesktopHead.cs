using Waylonia.Audio;
using Waylonia.Sessions;
using Waylonia.UI;
using static Waylonia.WayloniaLog;

namespace Waylonia;

internal static partial class DesktopHead
{
    public static HostCapabilities Capabilities => PlatformCapabilities();

    public static WayloniaPaths Paths() => PlatformPaths();

    public static int Run(WayloniaRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        var links = new TmdsSshLinkFactory(() => TimeSpan.FromSeconds(run.Host.SshTimeout), Log, run.Paths);
        return WayloniaApp.Run(run, Compose(run.Capabilities, run.Paths, links, OpenAudio));
    }

    private static IAudioSink? OpenAudio(AudioFill fill, int rate, int channels)
    {
        var sink = AudioSink.TryCreate(fill, rate, channels, out var whyNot);
        if (sink is null)
        {
            Log.Warn($"this host has no playback device, so no session has sound: {whyNot}");
        }

        return sink;
    }

    private static WayloniaPaths LocalApplicationDataPaths()
    {
        var xdg = WayloniaPaths.Xdg();
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return xdg with
        {
            StateFile = Path.Combine(local, "waylonia", "shell.toml"),
            IconCacheRoot = Path.Combine(WayloniaPaths.XdgHome("XDG_CACHE_HOME", local), "waylonia", "icons"),
        };
    }

    private static partial HostCapabilities PlatformCapabilities();

    private static partial WayloniaPaths PlatformPaths();

    private static partial HostPlatform Compose(
        HostCapabilities capabilities,
        WayloniaPaths paths,
        ISshLinkFactory links,
        Func<AudioFill, int, int, IAudioSink?> audio);
}
