using Android.Media;
using Waylonia.Audio;
using static Waylonia.WayloniaLog;

namespace Waylonia;

internal sealed class AudioTrackSink : IAudioSink
{
    private readonly AudioTrack _track;
    private readonly AudioFill _fill;
    private readonly float[] _buffer;
    private readonly Thread _thread;
    private volatile bool _stopping;
    private bool _disposed;

    private AudioTrackSink(AudioTrack track, AudioFill fill, int samples)
    {
        _track = track;
        _fill = fill;
        _buffer = new float[samples];
        _thread = new Thread(Feed)
        {
            Name = "waylonia-audio",
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal,
        };
    }

    public static AudioTrackSink? TryCreate(AudioFill fill, int rate, int channels, out string whyNot)
    {
        ArgumentNullException.ThrowIfNull(fill);
        var mask = channels == 1 ? ChannelOut.Mono : ChannelOut.Stereo;
        AudioTrack? track = null;
        try
        {
            var minimum = AudioTrack.GetMinBufferSize(rate, mask, Encoding.PcmFloat);
            var bytes = Math.Max(minimum, rate / 10 * channels * sizeof(float));
            var builder = new AudioTrack.Builder()
                .SetAudioAttributes(new AudioAttributes.Builder()
                    .SetUsage(AudioUsageKind.Media)!
                    .SetContentType(AudioContentType.Music)!
                    .Build()!)
                .SetAudioFormat(new AudioFormat.Builder()
                    .SetEncoding(Encoding.PcmFloat)!
                    .SetSampleRate(rate)!
                    .SetChannelMask(mask)!
                    .Build()!)
                .SetTransferMode(AudioTrackMode.Stream)
                .SetBufferSizeInBytes(bytes);
            if (OperatingSystem.IsAndroidVersionAtLeast(26))
            {
                builder.SetPerformanceMode(AudioTrackPerformanceMode.LowLatency);
            }

            track = builder.Build();
            if (track.State != AudioTrackState.Initialized)
            {
                whyNot = "the audio track did not initialize";
                track.Release();
                return null;
            }

            var sink = new AudioTrackSink(track, fill, Math.Max(channels, bytes / sizeof(float) / 4 / channels * channels));
            track.Play();
            sink._thread.Start();
            whyNot = string.Empty;
            return sink;
        }
        catch (Java.Lang.Exception error)
        {
            whyNot = error.Message ?? error.GetType().Name;
            track?.Release();
            return null;
        }
    }

    private void Feed()
    {
        while (!_stopping)
        {
            _fill(_buffer);
            var written = _track.Write(_buffer, 0, _buffer.Length, WriteMode.Blocking);
            if (written < 0)
            {
                Log.Warn($"the audio track stopped accepting samples (error {written}), so sound is off until the next session");
                return;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stopping = true;
        try
        {
            _track.Pause();
            _track.Flush();
        }
        catch (Java.Lang.Exception)
        {
        }

        if (_thread.IsAlive)
        {
            _thread.Join();
        }

        _track.Release();
    }
}
