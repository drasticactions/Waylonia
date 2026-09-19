using Basin.Capabilities;
using Basin.Video.MediaCodec;

namespace Waylonia;

internal sealed class AndroidVideoDecoders : IVideoDecoders
{
    public (IVideoDecoder? Decoder, string WhyNot) Create(bool hardware)
    {
        var decoder = MediaCodecVideoDecoder.TryCreate(out var whyNot);
        return (decoder, whyNot ?? string.Empty);
    }
}
