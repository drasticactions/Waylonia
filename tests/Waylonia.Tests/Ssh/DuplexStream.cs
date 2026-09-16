using System.IO.Pipelines;

namespace Waylonia.Tests.Ssh;

internal sealed class DuplexStream : Stream
{
    private readonly Pipe _incoming = new();
    private readonly Pipe _outgoing = new();
    private readonly Stream _reader;
    private readonly Stream _writer;

    public DuplexStream()
    {
        _reader = _incoming.Reader.AsStream();
        _writer = _outgoing.Writer.AsStream();
    }

    public PipeWriter Feed => _incoming.Writer;

    public PipeReader Sent => _outgoing.Reader;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override void Flush() => _writer.Flush();

    public override int Read(byte[] buffer, int offset, int count) => _reader.Read(buffer, offset, count);

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => _reader.ReadAsync(buffer, cancellationToken);

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => _writer.Write(buffer, offset, count);

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => _writer.WriteAsync(buffer, cancellationToken);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _incoming.Writer.Complete();
            _outgoing.Writer.Complete();
        }

        base.Dispose(disposing);
    }
}
