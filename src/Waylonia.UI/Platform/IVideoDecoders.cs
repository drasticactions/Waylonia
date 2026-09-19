using Basin.Capabilities;

namespace Waylonia;

internal interface IVideoDecoders
{
    (IVideoDecoder? Decoder, string WhyNot) Create(bool hardware);
}
