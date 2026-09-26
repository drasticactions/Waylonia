using System.Threading.Channels;
using Basin.Diagnostics;
using static Waylonia.Agent.AgentLog;

namespace Waylonia.Agent;

internal sealed class AgentAudit : IDisposable
{
    private readonly Channel<Func<StreamWriter, Task>> _work = Channel.CreateUnbounded<Func<StreamWriter, Task>>(
        new UnboundedChannelOptions { SingleReader = true });
    private readonly Task _writer;
    private int _shots;
    private bool _disposed;

    public AgentAudit(string directory, DateTimeOffset started, bool keepText)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        Directory = directory;
        KeepText = keepText;
        Path = System.IO.Path.Combine(directory, AgentAuditFormat.FileName(started, Environment.ProcessId));
        _writer = Task.Run(WriteAsync);
    }

    public string Directory { get; }

    public string Path { get; }

    public bool KeepText { get; }

    public string NextShot(string kind) =>
        $"{Interlocked.Increment(ref _shots).ToString("D6", System.Globalization.CultureInfo.InvariantCulture)}-{kind}.png";

    public void Line(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        _ = _work.Writer.TryWrite(async writer => await writer.WriteLineAsync(line).ConfigureAwait(false));
    }

    public void Shot(string name, byte[] rgba, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(rgba);
        var path = System.IO.Path.Combine(Directory, name);
        _ = _work.Writer.TryWrite(async _ =>
        {
            try
            {
                await File.WriteAllBytesAsync(path, PngCodec.Encode(rgba, width, height)).ConfigureAwait(false);
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                Log.Warn($"the audit screenshot {path} was not written: {failure.Message}");
            }
        });
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _work.Writer.TryComplete();
        if (!_writer.Wait(TimeSpan.FromSeconds(5)))
        {
            Log.Warn($"the audit log {Path} did not finish writing within 5 s");
        }
    }

    private async Task WriteAsync()
    {
        StreamWriter? writer = null;
        try
        {
            writer = new StreamWriter(Path, append: true) { AutoFlush = true };
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            Log.Error($"the audit log {Path} cannot be opened, so calls are not recorded: {failure.Message}");
        }

        try
        {
            await foreach (var item in _work.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                if (writer is null)
                {
                    continue;
                }

                try
                {
                    await item(writer).ConfigureAwait(false);
                }
                catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
                {
                    Log.Warn($"the audit log {Path} could not be written: {failure.Message}");
                }
            }
        }
        finally
        {
            if (writer is not null)
            {
                await writer.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
