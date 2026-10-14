using System.Net.Sockets;

namespace Waylonia.Sessions;

[System.Runtime.Versioning.SupportedOSPlatform("linux")]
internal sealed class VsockStream(int fd) : Stream
{
    private readonly Socket _socket = new(new SafeSocketHandle(fd, ownsHandle: true));
    private int _disposed;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        try
        {
            return _socket.Receive(buffer);
        }
        catch (SocketException error)
        {
            throw new IOException($"vsock read failed: {error.Message}", error);
        }
    }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        try
        {
            while (!buffer.IsEmpty)
            {
                buffer = buffer[_socket.Send(buffer)..];
            }
        }
        catch (SocketException error)
        {
            throw new IOException($"vsock write failed: {error.Message}", error);
        }
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0 && disposing)
        {
            try
            {
                _socket.Shutdown(SocketShutdown.Both);
            }
            catch (SocketException)
            {
            }

            _socket.Dispose();
        }

        base.Dispose(disposing);
    }
}
