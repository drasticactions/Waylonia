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
    IKeyPicker Keys,
    IReadOnlyDictionary<DestinationKind, ITargetLinkFactory>? Targets = null)
{
    public IReadOnlyDictionary<DestinationKind, ITargetLinkFactory> TargetLinks => Targets ?? NoTargets;

    private static readonly IReadOnlyDictionary<DestinationKind, ITargetLinkFactory> NoTargets =
        new Dictionary<DestinationKind, ITargetLinkFactory>();

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
