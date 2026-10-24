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
            SshSession.RemoteRuntimeDir +
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
            Complain($"audio {_sshHost}: nothing captured from {_monitor} in {SilenceMillis / 1000} s, the session has no sound");
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
                    Complain($"audio {_sshHost}: the capture channel did not start, the session has no sound", error);
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
                    Log.Debug(error, $"audio {_sshHost}: the capture's error output closed");
                }
            });
            try
            {
                await PumpAsync(capture.Output);
            }
            catch (Exception error) when (error is IOException or ObjectDisposedException or OperationCanceledException or InvalidOperationException)
            {
                Log.Debug(error, $"audio {_sshHost}: the capture stream closed");
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
                Complain($"audio {_sshHost}: the remote has neither parec nor pw-record, the session has no sound");
                return;
            }

            if (code == NoMonitorExit && _attempts >= Attempts - 1)
            {
                Complain($"audio {_sshHost}: the remote never created {_monitor}, the session has no sound");
                return;
            }

            if (!_delivered && ++_attempts == Attempts)
            {
                Complain($"audio {_sshHost}: nothing captured from {_monitor} in {Attempts} tries, the session has no sound");
                return;
            }

            Log.Debug($"audio {_sshHost}: the capture channel ended with {code}, opening it again");
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

    private void Complain(string message, Exception? error = null)
    {
        if (_complained)
        {
            return;
        }

        _complained = true;
        if (error is null)
        {
            Log.Warn($"{message}");
        }
        else
        {
            Log.Warn(error, $"{message}");
        }

        foreach (var line in _errors.Lines())
        {
            Log.Warn($"audio {_sshHost}: remote said {line}");
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
