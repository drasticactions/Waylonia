using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Transport.Waypipe;
using Waylonia.Cli;

namespace Waylonia.Sessions;

internal sealed record SessionSettings(
    string Name,
    string Ssh,
    string? Command,
    IReadOnlyList<string> Autostart,
    WaypipeCompression Compression,
    bool Gpu,
    bool Audio,
    string AudioFormat,
    string? Video,
    IVideoDecoder? VideoDecoder,
    string? Terminal,
    string? CurrentDesktop,
    string? Lang,
    IReadOnlyList<Hotkey> Hotkeys,
    DesktopRecipe? Desktop,
    IReadOnlyList<string> DesktopEnv,
    (int Width, int Height)? DesktopSize,
    bool AdHoc)
{
    public bool IsDesktop => Desktop is not null;

    public static Func<bool, (IVideoDecoder? Decoder, string WhyNot)> CreateDecoder { get; set; } = hardware =>
    {
        var decoder = Basin.Video.FFmpeg.FFmpegVideoDecoder.TryCreate(hardware, out var whyNot);
        return (decoder, whyNot ?? string.Empty);
    };

    public static SessionResolution Resolve(
        SessionProfile profile, SessionOverrides overrides, Config config, BasinLogger log, bool adHoc = false)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(overrides);
        ArgumentNullException.ThrowIfNull(config);
        DesktopRecipe? recipe = null;
        var desktopName = overrides.Desktop ?? profile.Desktop;
        if (desktopName is not null)
        {
            var found = DesktopRecipes.Find(desktopName);
            if (found is null && desktopName != "custom")
            {
                return new(null, $"desktop {desktopName} names no recipe; the built-in ones are {DesktopRecipes.Names}");
            }

            var desktopCommand = overrides.Command ?? profile.Command ?? found?.Command;
            if (desktopCommand is null)
            {
                return new(null, $"session {profile.Name} has desktop = \"custom\" and no command");
            }

            recipe = (found ?? new DesktopRecipe(
                desktopName, desktopCommand, desktopName.ToUpperInvariant(),
                [], Bus: true, Gpu: true, Video: null, SoftwareFallback: false))
                with { Command = desktopCommand };
        }

        (int Width, int Height)? desktopSize = null;
        var sizeText = overrides.DesktopSize ?? profile.DesktopSize;
        if (sizeText is not null)
        {
            desktopSize = RunRules.ParseSize(sizeText);
            if (desktopSize is null)
            {
                return new(null, $"desktop-size takes WxH, not '{sizeText}'");
            }
        }

        var compress = overrides.Compress ?? profile.Compress ?? config.Compress ?? "lz4";
        var gpu = overrides.Gpu ?? profile.Gpu ?? recipe?.Gpu ?? config.Gpu ?? false;
        var audio = overrides.Audio ?? profile.Audio ?? config.Audio ?? false;
        var video = overrides.Video ?? profile.Video ?? recipe?.Video ?? config.Video ?? "none";
        if (!VideoChoice.IsValid(video))
        {
            return new(null, $"video '{video}' is not a codec choice such as h264,hw");
        }

        var videoCodec = video.Split(',')[0];
        var videoRemote = VideoChoice.RemoteSetting(video);
        IVideoDecoder? decoder = null;
        if (videoCodec != "none")
        {
            gpu = true;
            var (created, whyNot) = CreateDecoder(VideoChoice.DecodesOnGpu(video));
            if (created is null)
            {
                return new(null, $"video {video} needs a decoder and none is available: {whyNot}");
            }

            var wanted = videoCodec switch
            {
                "vp9" => VideoCodec.Vp9,
                "av1" => VideoCodec.Av1,
                _ => VideoCodec.H264,
            };
            if (!created.Supports(wanted))
            {
                return new(null, $"the system FFmpeg decodes no {videoCodec}");
            }

            decoder = created;
        }

        var lang = profile.Lang ?? config.Lang;
        return new(new SessionSettings(
            profile.Name,
            profile.Ssh,
            recipe is null ? overrides.Command ?? profile.Command : null,
            profile.Autostart ?? [],
            compress switch
            {
                "none" => WaypipeCompression.None,
                "zstd" => WaypipeCompression.Zstd,
                _ => WaypipeCompression.Lz4,
            },
            gpu,
            audio,
            overrides.AudioFormat,
            videoCodec == "none" ? null : videoRemote is null ? videoCodec : $"{videoCodec},{videoRemote}",
            decoder,
            profile.Terminal ?? config.Terminal,
            profile.CurrentDesktop ?? config.CurrentDesktop,
            lang.Length > 0 ? lang : null,
            profile.Hotkeys ?? [],
            recipe,
            profile.DesktopEnv ?? [],
            desktopSize,
            adHoc), null);
    }
}
