using Waylonia.Audio;
using Waylonia.Tests.Ssh;
using Xunit;

namespace Waylonia.Tests;

public sealed class RemoteAudioSourceTests
{
    private static async Task WaitUntilAsync(Func<bool> condition, string what)
    {
        for (var i = 0; i < 500 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition(), what);
    }

    private static (RemoteAudioSource Source, FakeSshLink Link, AudioRing Ring) Start(bool sixteenBit = false)
    {
        var ring = AudioRing.ForSession(48000, 2);
        var link = new FakeSshLink("user@dev", new FakePrompter());
        var source = new RemoteAudioSource(ring, "user@dev", link, "waylonia-1-1", 48000, 2, sixteenBit, initialBackoff: 10);
        source.Start();
        return (source, link, ring);
    }

    [Fact]
    public async Task Samples_arriving_over_the_capture_channel_land_in_the_ring()
    {
        var (source, link, ring) = Start(sixteenBit: true);
        await WaitUntilAsync(() => link.Commands.Count == 1, "the capture started");
        var capture = link.Command(0);
        Assert.Contains("parec --format=s16le --rate=48000 --channels=2", capture.Script, StringComparison.Ordinal);

        var pcm = new byte[4 * 6000];
        for (var i = 0; i < pcm.Length; i += 2)
        {
            pcm[i] = 0x00;
            pcm[i + 1] = 0x40;
        }

        await capture.WriteBytesAsync(pcm);
        await WaitUntilAsync(() => ring.Depth >= 12000, "samples reached the ring");

        var samples = new float[960];
        Assert.Equal(960, ring.Read(samples));
        Assert.Equal(0.5f, samples[0], 3);
        source.Dispose();
        Assert.True(capture.Disposed);
    }

    [Fact]
    public async Task A_missing_capture_tool_complains_once_and_stops()
    {
        using var log = new LogCapture();
        var (source, link, _) = Start();
        await WaitUntilAsync(() => link.Commands.Count == 1, "the capture started");
        link.Command(0).WriteError("no parec and no pw-record");
        link.Command(0).Exit(RemoteAudioSource.NoCaptureToolExit);

        await source.Completed;
        Assert.True(source.Complained);
        Assert.Single(link.Commands);
        Assert.Contains(log.Lines, line => line.Contains("has neither parec nor pw-record", StringComparison.Ordinal));
        source.Dispose();
    }

    [Fact]
    public async Task A_missing_monitor_is_retried_with_backoff_then_given_up()
    {
        using var log = new LogCapture();
        var (source, link, _) = Start();
        for (var attempt = 0; attempt < RemoteAudioSource.Attempts; attempt++)
        {
            var expected = attempt + 1;
            await WaitUntilAsync(() => link.Commands.Count == expected, $"capture attempt {expected} started");
            link.Command(attempt).Exit(RemoteAudioSource.NoMonitorExit);
        }

        await source.Completed;
        Assert.Equal(RemoteAudioSource.Attempts, link.Commands.Count);
        Assert.Contains(log.Lines, line => line.Contains("never created waylonia-1-1.monitor", StringComparison.Ordinal));
        source.Dispose();
    }

    [Fact]
    public async Task Losing_the_link_stops_the_capture_without_a_retry()
    {
        var (source, link, _) = Start();
        await WaitUntilAsync(() => link.Commands.Count == 1, "the capture started");

        link.Drop();
        await source.Completed;

        Assert.Single(link.Commands);
        Assert.False(source.Complained);
        source.Dispose();
    }
}
