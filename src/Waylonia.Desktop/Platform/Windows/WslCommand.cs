using System.Diagnostics;
using System.Threading.Channels;

namespace Waylonia.Sessions;

internal sealed class WslCommand : ISshCommand
{
    private readonly Process _process;
    private readonly string _distro;
    private readonly Channel<string> _stderr = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = false, SingleWriter = true });
    private readonly TaskCompletionSource<int> _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _pid;
    private bool _disposed;

    public WslCommand(string distro, string script)
    {
        _distro = distro;
        _process = WslProcess.Start(WslDestination.Script(distro, script), input: true);
        _ = PumpErrorsAsync();
        _ = WatchExitAsync();
    }

    public Stream Output => _process.StandardOutput.BaseStream;

    public Stream Input => _process.StandardInput.BaseStream;

    public Task<int> Exited => _exited.Task;

    public IAsyncEnumerable<string> ErrorLines(CancellationToken cancellation) => _stderr.Reader.ReadAllAsync(cancellation);

    public bool TrySignal(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var pid = Volatile.Read(ref _pid);
        if (_disposed || _exited.Task.IsCompleted || pid <= 0)
        {
            return false;
        }

        _ = WslProcess.RunAsync(WslDestination.Signal(_distro, name, pid), CancellationToken.None);
        return true;
    }

    private async Task PumpErrorsAsync()
    {
        try
        {
            while (await _process.StandardError.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                if (_pid == 0 && PidMarker.Parse(line) is { } pid)
                {
                    Volatile.Write(ref _pid, pid);
                    continue;
                }

                _stderr.Writer.TryWrite(line);
            }
        }
        catch (Exception error) when (error is IOException or ObjectDisposedException or InvalidOperationException)
        {
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
        }

        _exited.TrySetResult(code);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        WslProcess.Kill(_process);
        _process.Dispose();
    }
}
