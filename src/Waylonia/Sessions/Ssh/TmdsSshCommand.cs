using System.IO.Pipelines;
using System.Text;
using System.Threading.Channels;
using Tmds.Ssh;

namespace Waylonia.Sessions;

internal sealed class TmdsSshCommand : ISshCommand
{
    private readonly RemoteProcess _process;
    private readonly Pipe _stdout = new();
    private readonly Channel<string> _stderr = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = false, SingleWriter = true });
    private readonly TaskCompletionSource<int> _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Decoder _decoder = new UTF8Encoding(false, false).GetDecoder();
    private readonly StringBuilder _partial = new();
    private bool _disposed;

    public TmdsSshCommand(RemoteProcess process)
    {
        _process = process;
        Output = _stdout.Reader.AsStream();
        _ = PumpAsync();
    }

    public Stream Output { get; }

    public Stream Input => _process.StandardInputStream;

    public Task<int> Exited => _exited.Task;

    public IAsyncEnumerable<string> ErrorLines(CancellationToken cancellation) => _stderr.Reader.ReadAllAsync(cancellation);

    public bool TrySignal(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        try
        {
            return !_disposed && _process.SendSignal(name);
        }
        catch (Exception error) when (error is ObjectDisposedException or SshException or InvalidOperationException)
        {
            return false;
        }
    }

    private async Task PumpAsync()
    {
        var code = -1;
        try
        {
            await _process.ReadToEndAsync(WriteStdoutAsync, this, WriteStderrAsync, this).ConfigureAwait(false);
            var status = await _process.GetExitStatusAsync().ConfigureAwait(false);
            code = status.ExitCode;
        }
        catch (Exception error) when (error is SshException or ObjectDisposedException or InvalidOperationException or OperationCanceledException or IOException)
        {
        }

        FlushPartialLine();
        _stderr.Writer.TryComplete();
        await _stdout.Writer.CompleteAsync().ConfigureAwait(false);
        _exited.TrySetResult(code);
    }

    private static async ValueTask WriteStdoutAsync(Memory<byte> bytes, object? context, CancellationToken cancellation)
    {
        if (bytes.Length == 0)
        {
            return;
        }

        var self = (TmdsSshCommand)context!;
        await self._stdout.Writer.WriteAsync(bytes, cancellation).ConfigureAwait(false);
    }

    private static ValueTask WriteStderrAsync(Memory<byte> bytes, object? context, CancellationToken cancellation)
    {
        var self = (TmdsSshCommand)context!;
        self.SplitLines(bytes.Span);
        return ValueTask.CompletedTask;
    }

    private void SplitLines(ReadOnlySpan<byte> bytes)
    {
        Span<char> chars = stackalloc char[Math.Max(16, bytes.Length + 1)];
        var written = _decoder.GetChars(bytes, chars, false);
        foreach (var c in chars[..written])
        {
            if (c == '\n')
            {
                if (_partial.Length > 0 && _partial[^1] == '\r')
                {
                    _partial.Length--;
                }

                _stderr.Writer.TryWrite(_partial.ToString());
                _partial.Clear();
            }
            else
            {
                _partial.Append(c);
            }
        }
    }

    private void FlushPartialLine()
    {
        if (_partial.Length > 0)
        {
            _stderr.Writer.TryWrite(_partial.ToString());
            _partial.Clear();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _process.Dispose();
    }
}
