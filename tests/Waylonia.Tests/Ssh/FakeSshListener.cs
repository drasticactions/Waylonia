using System.Threading.Channels;
using Waylonia.Sessions;

namespace Waylonia.Tests.Ssh;

internal sealed class FakeSshListener(string remotePath) : ISshListener
{
    private readonly Channel<Stream> _streams = Channel.CreateUnbounded<Stream>();

    public string RemotePath => remotePath;

    public bool Disposed { get; private set; }

    public void Inject(Stream stream) => _streams.Writer.TryWrite(stream);

    public void Stop() => _streams.Writer.TryComplete();

    public async ValueTask<Stream?> AcceptAsync(CancellationToken cancellation)
    {
        try
        {
            return await _streams.Reader.ReadAsync(cancellation);
        }
        catch (ChannelClosedException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        Disposed = true;
        Stop();
    }
}
