using AudioToolbox;
using AVFoundation;
using ObjCRuntime;
using Waylonia.Audio;

namespace Waylonia;

internal sealed class AVAudioSink : IAudioSink
{
    private readonly AVAudioEngine _engine;
    private readonly AVAudioSourceNode _source;
    private readonly AudioFill _fill;
    private readonly int _channels;
    private float[] _interleaved = [];
    private bool _disposed;

    private AVAudioSink(AVAudioEngine engine, AVAudioFormat format, AudioFill fill, int channels)
    {
        _engine = engine;
        _fill = fill;
        _channels = channels;
        _source = new AVAudioSourceNode(format, Render);
    }

    public static AVAudioSink? TryCreate(AudioFill fill, int rate, int channels, out string whyNot)
    {
        ArgumentNullException.ThrowIfNull(fill);
        AVAudioSink? sink = null;
        try
        {
            var session = AVAudioSession.SharedInstance();
            var sessionError = session.SetCategory(AVAudioSessionCategory.Playback) ?? session.SetActive(true);
            if (sessionError is not null)
            {
                whyNot = sessionError.LocalizedDescription;
                return null;
            }

            var format = new AVAudioFormat(rate, (uint)channels);
            var engine = new AVAudioEngine();
            sink = new AVAudioSink(engine, format, fill, channels);
            engine.AttachNode(sink._source);
            engine.Connect(sink._source, engine.MainMixerNode, format);
            engine.Prepare();
            if (!engine.StartAndReturnError(out var error))
            {
                whyNot = error?.LocalizedDescription ?? "the audio engine did not start";
                sink.Dispose();
                return null;
            }
        }
        catch (ObjCException error)
        {
            whyNot = error.Message;
            sink?.Dispose();
            return null;
        }

        whyNot = string.Empty;
        return sink;
    }

    private unsafe int Render(ref bool isSilence, ref AudioTimeStamp timestamp, uint frameCount, AudioBuffers outputData)
    {
        var frames = (int)frameCount;
        var buffers = outputData.Count;
        if (_disposed || buffers == 0 || frames == 0)
        {
            isSilence = true;
            return 0;
        }

        var samples = frames * _channels;
        if (_interleaved.Length < samples)
        {
            _interleaved = new float[samples];
        }

        var interleaved = _interleaved.AsSpan(0, samples);
        _fill(interleaved);
        for (var channel = 0; channel < _channels && channel < buffers; channel++)
        {
            var buffer = outputData[channel];
            var output = new Span<float>((void*)buffer.Data, Math.Min(frames, buffer.DataByteSize / sizeof(float)));
            for (var frame = 0; frame < output.Length; frame++)
            {
                output[frame] = interleaved[(frame * _channels) + channel];
            }
        }

        isSilence = false;
        return 0;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _engine.Stop();
        _engine.DetachNode(_source);
        _source.Dispose();
        _engine.Dispose();
    }
}
