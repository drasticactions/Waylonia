using Basin.Capabilities;
using Basin.Video.FFmpeg;

namespace Waylonia;

internal sealed class FFmpegVideoDecoders : IVideoDecoders
{
    public (IVideoDecoder? Decoder, string WhyNot) Create(bool hardware)
    {
        var decoder = FFmpegVideoDecoder.TryCreate(hardware, out var whyNot);
        return (decoder, whyNot ?? string.Empty);
    }
}
