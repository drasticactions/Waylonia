using static Waylonia.WayloniaLog;

namespace Waylonia.Audio;

internal sealed class AudioMixer : IDisposable
{
    public const int Rate = 48000;

    public const int Channels = 2;

    private readonly object _gate = new();
    private readonly Func<AudioFill, AudioSink?> _open;
    private AudioRing[] _rings = [];
    private float[] _scratch = [];
    private AudioSink? _sink;

    public AudioMixer()
        : this(fill =>
        {
            var sink = AudioSink.TryCreate(fill, Rate, Channels, out var whyNot);
            if (sink is null)
            {
                Log.Warn($"this host has no playback device, so no session has sound: {whyNot}");
            }

            return sink;
        })
    {
    }

    public AudioMixer(Func<AudioFill, AudioSink?> open) => _open = open;

    public int Count => _rings.Length;

    public bool HasDevice => _sink is not null;

    public bool Add(AudioRing ring)
    {
        ArgumentNullException.ThrowIfNull(ring);
        lock (_gate)
        {
            if (Array.IndexOf(_rings, ring) < 0)
            {
                _rings = [.. _rings, ring];
            }

            _sink ??= _open(Fill);

            return _sink is not null;
        }
    }

    public void Remove(AudioRing ring)
    {
        ArgumentNullException.ThrowIfNull(ring);
        AudioSink? closing = null;
        lock (_gate)
        {
            var index = Array.IndexOf(_rings, ring);
            if (index < 0)
            {
                return;
            }

            var rings = new AudioRing[_rings.Length - 1];
            Array.Copy(_rings, 0, rings, 0, index);
            Array.Copy(_rings, index + 1, rings, index, _rings.Length - index - 1);
            _rings = rings;
            if (rings.Length == 0)
            {
                closing = _sink;
                _sink = null;
            }
        }

        closing?.Dispose();
    }

    public void Fill(Span<float> output)
    {
        var rings = Volatile.Read(ref _rings);
        if (rings.Length == 0)
        {
            output.Clear();
            return;
        }

        if (rings.Length == 1)
        {
            rings[0].Read(output);
            return;
        }

        var scratch = _scratch;
        if (scratch.Length < output.Length)
        {
            scratch = new float[output.Length];
            _scratch = scratch;
        }

        var mix = scratch.AsSpan(0, output.Length);
        output.Clear();
        foreach (var ring in rings)
        {
            if (ring.Read(mix) == 0)
            {
                continue;
            }

            for (var i = 0; i < output.Length; i++)
            {
                output[i] += mix[i];
            }
        }

        for (var i = 0; i < output.Length; i++)
        {
            output[i] = Math.Clamp(output[i], -1f, 1f);
        }
    }

    public void Dispose()
    {
        AudioSink? closing;
        lock (_gate)
        {
            closing = _sink;
            _sink = null;
            _rings = [];
        }

        closing?.Dispose();
    }
}
