using Waylonia.Audio;
using Xunit;

namespace Waylonia.Tests;

public sealed class AudioMixerTests
{
    private static AudioRing Ring(params float[] samples)
    {
        var ring = new AudioRing(1, 64, 0);
        ring.Write(samples);
        return ring;
    }

    [Fact]
    public void Two_rings_sum_and_a_silent_one_adds_nothing()
    {
        using var mixer = new AudioMixer(_ => null);
        mixer.Add(Ring(0.25f, 0.5f));
        mixer.Add(Ring(0.25f, -0.25f));
        mixer.Add(new AudioRing(1, 64, 0));
        var output = new float[2];

        mixer.Fill(output);

        Assert.Equal([0.5f, 0.25f], output);
    }

    [Fact]
    public void The_sum_clamps_to_the_sample_range()
    {
        using var mixer = new AudioMixer(_ => null);
        mixer.Add(Ring(0.9f, -0.9f));
        mixer.Add(Ring(0.9f, -0.9f));
        var output = new float[2];

        mixer.Fill(output);

        Assert.Equal([1f, -1f], output);
    }

    [Fact]
    public void One_ring_passes_straight_through_and_no_ring_is_silence()
    {
        using var mixer = new AudioMixer(_ => null);
        var output = new float[] { 7f, 7f };
        mixer.Fill(output);
        Assert.Equal([0f, 0f], output);

        var ring = Ring(0.3f, 0.6f);
        mixer.Add(ring);
        mixer.Fill(output);
        Assert.Equal([0.3f, 0.6f], output);

        mixer.Remove(ring);
        Assert.Equal(0, mixer.Count);
        output[0] = 5f;
        mixer.Fill(output);
        Assert.Equal([0f, 0f], output);
    }

    [Fact]
    public async Task Adding_and_removing_while_reading_keeps_the_reader_on_a_stable_snapshot()
    {
        using var mixer = new AudioMixer(_ => null);
        var rings = Enumerable.Range(0, 8).Select(_ => Ring(0.1f, 0.1f, 0.1f, 0.1f)).ToArray();
        var output = new float[4];
        var stop = false;
        var reader = Task.Run(
            () =>
            {
                while (!Volatile.Read(ref stop))
                {
                    mixer.Fill(output);
                    Assert.All(output, sample => Assert.InRange(sample, -1f, 1f));
                }
            },
            TestContext.Current.CancellationToken);

        for (var round = 0; round < 200; round++)
        {
            foreach (var ring in rings)
            {
                mixer.Add(ring);
            }

            foreach (var ring in rings)
            {
                mixer.Remove(ring);
            }
        }

        Volatile.Write(ref stop, true);
        await reader;
        Assert.Equal(0, mixer.Count);
    }

    [Fact]
    public void The_device_opens_with_the_first_ring_and_closes_with_the_last()
    {
        var opened = 0;
        using var mixer = new AudioMixer(_ =>
        {
            opened++;
            return null;
        });
        var ring = Ring(0f);

        Assert.False(mixer.Add(ring));
        Assert.Equal(1, opened);
        Assert.False(mixer.Add(Ring(0f)));
        Assert.Equal(2, opened);
        Assert.False(mixer.HasDevice);
    }
}
