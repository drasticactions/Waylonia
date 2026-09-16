using System.Runtime.InteropServices;
using Waylonia.Sessions;
using static Waylonia.WayloniaLog;

namespace Waylonia.Audio;

internal sealed class RemoteAudioSource : IDisposable
{
    private const int ReadBytes = 16 * 1024;

    public const int Attempts = 5;

    public const int NoMonitorExit = 126;

    public const int NoCaptureToolExit = 127;

    private const int SilenceMillis = 10_000;

    private readonly AudioRing _ring;
    private readonly string _sshHost;
    private readonly ISshLink _link;
    private readonly string _remote;
    private readonly int _bytesPerFrame;
    private readonly bool _sixteenBit;
    private readonly CancellationTokenSource _stopping;
    private readonly byte[] _bytes = new byte[ReadBytes];
    private readonly float[] _samples = new float[ReadBytes / 2];
    private readonly string _monitor;
    private readonly int _initialBackoff;

    private ISshCommand? _capture;
    private OutputTail _errors = new();
    private bool _complained;
    private bool _delivered;
    private int _attempts;

    public RemoteAudioSource(
        AudioRing ring,
        string sshHost,
        ISshLink link,
        string sink,
        int rate,
        int channels,
        bool sixteenBit,
        int initialBackoff = 500)
    {
        ArgumentNullException.ThrowIfNull(ring);
        ArgumentNullException.ThrowIfNull(link);
        _ring = ring;
        _sshHost = sshHost;
        _link = link;
        _sixteenBit = sixteenBit;
        _bytesPerFrame = channels * (sixteenBit ? 2 : 4);
        _monitor = $"{sink}.monitor";
        _initialBackoff = initialBackoff;
        _stopping = CancellationTokenSource.CreateLinkedTokenSource(link.Lost);
        var parecFormat = sixteenBit ? "s16le" : "float32le";
        var pipewireFormat = sixteenBit ? "s16" : "f32";
        _remote =
            "if command -v pactl >/dev/null 2>&1 && " +
            $"! pactl list short sources 2>/dev/null | cut -f2 | grep -qx {_monitor}; then " +
            $"echo 'no {_monitor}' >&2; exit {NoMonitorExit}; fi; " +
            "if command -v parec >/dev/null 2>&1; then " +
            $"exec parec --format={parecFormat} --rate={rate} --channels={channels} " +
            $"--latency-msec=50 -d {_monitor}; " +
            "elif command -v pw-record >/dev/null 2>&1; then " +
            $"exec pw-record --raw --format={pipewireFormat} --rate={rate} --channels={channels} " +
            $"--target={_monitor} -; " +
            $"else echo 'no parec and no pw-record' >&2; exit {NoCaptureToolExit}; fi";
    }

    public string Script => _remote;

    public bool Complained => _complained;

    public Task Completed { get; private set; } = Task.CompletedTask;

    public void Start()
    {
        Completed = Task.Run(RunAsync);
        _ = Task.Run(WatchSilenceAsync);
    }

    private async Task WatchSilenceAsync()
    {
        try
        {
            await Task.Delay(SilenceMillis, _stopping.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!_delivered)
        {
            Complain($"nothing has been captured from {_monitor} on {_sshHost}, so the session has no sound");
        }
    }

    private async Task RunAsync()
    {
        var backoff = _initialBackoff;
        while (!_stopping.IsCancellationRequested)
        {
            var errors = new OutputTail();
            _errors = errors;
            ISshCommand capture;
            try
            {
                capture = await _link.RunAsync(_remote, _stopping.Token);
            }
            catch (SshLinkException error)
            {
                if (!_stopping.IsCancellationRequested)
                {
                    Complain($"the capture channel to {_sshHost} could not start: {error.Message}");
                }

                return;
            }
            catch (OperationCanceledException)
            {
                return;
            }

            _capture = capture;
            var complaints = Task.Run(async () =>
            {
                try
                {
                    await foreach (var line in capture.ErrorLines(CancellationToken.None))
                    {
                        errors.Add(line);
                    }
                }
                catch (Exception error) when (error is OperationCanceledException or ObjectDisposedException)
                {
                }
            });
            try
            {
                await PumpAsync(capture.Output);
            }
            catch (Exception error) when (error is IOException or ObjectDisposedException or OperationCanceledException or InvalidOperationException)
            {
            }

            var code = await capture.Exited;
            await complaints;
            capture.Dispose();
            _capture = null;
            if (_stopping.IsCancellationRequested)
            {
                return;
            }

            if (code == NoCaptureToolExit)
            {
                Complain($"{_sshHost} has neither parec nor pw-record, so the session has no sound");
                return;
            }

            if (code == NoMonitorExit && _attempts >= Attempts - 1)
            {
                Complain($"{_sshHost} never created {_monitor}, so the session has no sound");
                return;
            }

            if (!_delivered && ++_attempts == Attempts)
            {
                Complain(
                    $"nothing was captured from {_monitor} on {_sshHost} in {Attempts} tries, " +
                    "so the session has no sound");
                return;
            }

            Log.Debug($"the capture channel to {_sshHost} ended with {code}; opening it again");
            try
            {
                await Task.Delay(backoff, _stopping.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            backoff = Math.Min(backoff * 2, 4000);
        }
    }

    private async Task PumpAsync(Stream pcm)
    {
        var carried = 0;
        while (!_stopping.IsCancellationRequested)
        {
            var read = await pcm.ReadAsync(_bytes.AsMemory(carried), _stopping.Token);
            if (read == 0)
            {
                return;
            }

            var total = carried + read;
            var whole = total - (total % _bytesPerFrame);
            if (whole > 0)
            {
                _delivered = true;
                _ring.Write(Decode(_bytes.AsSpan(0, whole)));
            }

            carried = total - whole;
            if (carried > 0)
            {
                _bytes.AsSpan(whole, carried).CopyTo(_bytes);
            }
        }
    }

    private ReadOnlySpan<float> Decode(ReadOnlySpan<byte> pcm)
    {
        if (!_sixteenBit)
        {
            return MemoryMarshal.Cast<byte, float>(pcm);
        }

        var source = MemoryMarshal.Cast<byte, short>(pcm);
        var destination = _samples.AsSpan(0, source.Length);
        for (var i = 0; i < source.Length; i++)
        {
            destination[i] = source[i] / 32768f;
        }

        return destination;
    }

    private void Complain(string message)
    {
        if (_complained)
        {
            return;
        }

        _complained = true;
        Log.Warn($"{message}");
        foreach (var line in _errors.Lines())
        {
            Log.Error($"audio: {line}");
        }
    }

    public void Dispose()
    {
        if (!_stopping.IsCancellationRequested)
        {
            _stopping.Cancel();
        }

        if (_capture is { } capture)
        {
            capture.TrySignal("TERM");
            capture.Dispose();
            _capture = null;
        }

        _stopping.Dispose();
    }
}
