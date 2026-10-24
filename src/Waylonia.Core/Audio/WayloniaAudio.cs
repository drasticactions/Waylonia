using Waylonia.Sessions;
using static Waylonia.WayloniaLog;

namespace Waylonia.Audio;

internal sealed class WayloniaAudio : IDisposable
{
    public const int Rate = AudioMixer.Rate;

    public const int Channels = AudioMixer.Channels;

    private readonly AudioRing _ring;
    private readonly AudioMixer _mixer;
    private readonly RemoteAudioSource _source;

    private WayloniaAudio(AudioRing ring, AudioMixer mixer, RemoteAudioSource source)
    {
        _ring = ring;
        _mixer = mixer;
        _source = source;
    }

    public AudioRing Ring => _ring;

    public static bool Wanted(bool audio, string? sshHost, string? waypipeListen)
    {
        if (!audio)
        {
            return false;
        }

        if (waypipeListen is not null)
        {
            Log.Warn($"audio: --audio is ignored with --waypipe-listen, which opens no connection to carry the sound");
            return false;
        }

        if (sshHost is null)
        {
            Log.Warn($"audio: --audio is ignored for a local session, whose applications already play through this host's sound server");
            return false;
        }

        return true;
    }

    public static WayloniaAudio? TryStart(AudioMixer mixer, string sshHost, ISshLink link, string sink, string format)
    {
        ArgumentNullException.ThrowIfNull(mixer);
        ArgumentNullException.ThrowIfNull(link);
        var ring = AudioRing.ForSession(Rate, Channels);
        if (!mixer.Add(ring))
        {
            mixer.Remove(ring);
            Log.Warn($"audio {sshHost}: no sound, this host has no playback device");
            return null;
        }

        var source = new RemoteAudioSource(ring, sshHost, link, sink, Rate, Channels, format == "s16");
        source.Start();
        Log.Info($"audio {sshHost}: playing from {sink}.monitor as {format}");
        return new WayloniaAudio(ring, mixer, source);
    }

    public void Dispose()
    {
        _source.Dispose();
        _mixer.Remove(_ring);
        Log.Debug($"audio: ended, {_ring.Underruns} underrun(s), {_ring.Dropped} sample(s) dropped");
    }
}
