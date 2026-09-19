using Basin.Capabilities;
using Waylonia.Sessions;

namespace Waylonia;

internal sealed class NullVideoDecoders : IVideoDecoders
{
    public static readonly NullVideoDecoders Instance = new();

    public (IVideoDecoder? Decoder, string WhyNot) Create(bool hardware) => (null, SessionSettings.NoDecoder);
}
