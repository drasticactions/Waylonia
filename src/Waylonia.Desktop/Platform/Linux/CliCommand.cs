using System.Diagnostics;
using System.Threading.Channels;
using static Waylonia.WayloniaLog;

namespace Waylonia.Sessions;

[System.Runtime.Versioning.SupportedOSPlatform("linux")]
internal sealed class CliCommand : ISshCommand
{
    private readonly Process _process;
    private readonly Func<int, string, bool> _signal;
    private readonly Channel<string> _stderr = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = false, SingleWriter = true });
    private readonly TaskCompletionSource<int> _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<int> _pid = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _disposed;

    public CliCommand(Process process, Func<int, string, bool> signal)
    {
        _process = process;
        _signal = signal;
        _ = PumpErrorsAsync();
        _ = WatchExitAsync();
    }

    public Stream Output => _process.StandardOutput.BaseStream;

    public Stream Input => _process.StandardInput.BaseStream;

    public Task<int> Exited => _exited.Task;

    public Task<int> Pid => _pid.Task;

    public IAsyncEnumerable<string> ErrorLines(CancellationToken cancellation) => _stderr.Reader.ReadAllAsync(cancellation);

    public bool TrySignal(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (_disposed || _exited.Task.IsCompleted || !_pid.Task.IsCompletedSuccessfully)
        {
            return false;
        }

        return _signal(_pid.Task.Result, name);
    }

    private async Task PumpErrorsAsync()
    {
        try
        {
            while (await _process.StandardError.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                if (!_pid.Task.IsCompleted && PidMarker.Parse(line) is { } pid)
                {
                    _pid.TrySetResult(pid);
                    continue;
                }

                _stderr.Writer.TryWrite(line);
            }
        }
        catch (Exception error) when (error is IOException or ObjectDisposedException or InvalidOperationException)
        {
            Log.Debug(error, $"command: standard error closed");
        }

        _stderr.Writer.TryComplete();
    }

    private async Task WatchExitAsync()
    {
        var code = -1;
        try
        {
            await _process.WaitForExitAsync().ConfigureAwait(false);
            code = _process.ExitCode;
        }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Log.Debug(error, $"command: exit code not available");
        }

        _pid.TrySetResult(0);
        _exited.TrySetResult(code);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (!_exited.Task.IsCompleted && _pid.Task.IsCompletedSuccessfully && _pid.Task.Result > 0)
        {
            _signal(_pid.Task.Result, "KILL");
        }

        _disposed = true;
        try
        {
            _process.StandardInput.Close();
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or ObjectDisposedException)
        {
            Log.Debug(error, $"command: standard input already closed");
        }

        CliProcess.Kill(_process);
        _process.Dispose();
    }
}
