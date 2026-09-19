using Waylonia.Audio;
using Waylonia.Sessions;

namespace Waylonia;

internal sealed record HostPlatform(
    HostCapabilities Capabilities,
    WayloniaPaths Paths,
    ISshLinkFactory Links,
    Func<AudioFill, int, int, IAudioSink?> OpenAudio,
    IVideoDecoders Video,
    IHostCursor Cursor,
    IHostScreenScales ScreenScales,
    IHostWindowing Windowing,
    IKeyPicker Keys)
{
    public static HostPlatform Minimal(WayloniaPaths paths, ISshLinkFactory links)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(links);
        return new(
            HostCapabilities.None,
            paths,
            links,
            static (_, _, _) => null,
            NullVideoDecoders.Instance,
            NullHostCursor.Instance,
            NullHostScreenScales.Instance,
            NullHostWindowing.Instance,
            NullKeyPicker.Instance);
    }
}
