using Basin.Capabilities;
using Basin.Video.VideoToolbox;

namespace Waylonia;

internal sealed class IosVideoDecoders : IVideoDecoders
{
    public (IVideoDecoder? Decoder, string WhyNot) Create(bool hardware)
    {
        var decoder = VideoToolboxVideoDecoder.TryCreate(out var whyNot);
        return (decoder, whyNot ?? string.Empty);
    }
}
