using System.IO.Pipelines;
using System.Text;
using System.Threading.Channels;
using Waylonia.Sessions;

namespace Waylonia.Tests.Ssh;

internal sealed class FakeSshCommand(string script) : ISshCommand
{
    private readonly Pipe _stdout = new();
    private readonly Pipe _stdin = new();
    private readonly Channel<string> _stderr = Channel.CreateUnbounded<string>();
    private readonly TaskCompletionSource<int> _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public string Script => script;

    public Stream Output => _stdout.Reader.AsStream();

    public Stream Input => _stdin.Writer.AsStream();

    public Stream InputRead => _stdin.Reader.AsStream();

    public Task<int> Exited => _exited.Task;

    public List<string> Signals { get; } = [];

    public bool Disposed { get; private set; }

    public IAsyncEnumerable<string> ErrorLines(CancellationToken cancellation) => _stderr.Reader.ReadAllAsync(cancellation);

    public async Task WriteOutputAsync(string text)
    {
        await _stdout.Writer.WriteAsync(Encoding.UTF8.GetBytes(text));
        await _stdout.Writer.FlushAsync();
    }

    public async Task WriteBytesAsync(byte[] bytes)
    {
        await _stdout.Writer.WriteAsync(bytes);
        await _stdout.Writer.FlushAsync();
    }

    public void WriteError(string line) => _stderr.Writer.TryWrite(line);

    public void Exit(int code)
    {
        if (_exited.TrySetResult(code))
        {
            _stderr.Writer.TryComplete();
            _stdout.Writer.Complete();
        }
    }

    public bool TrySignal(string name)
    {
        if (Disposed || _exited.Task.IsCompleted)
        {
            return false;
        }

        Signals.Add(name);
        return true;
    }

    public void Dispose()
    {
        Disposed = true;
        Exit(-1);
    }
}
