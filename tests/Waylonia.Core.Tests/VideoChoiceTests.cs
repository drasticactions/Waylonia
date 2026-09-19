using Waylonia.Cli;
using Xunit;

namespace Waylonia.Tests;

public sealed class VideoChoiceTests
{
    [Theory]
    [InlineData("none")]
    [InlineData("h264")]
    [InlineData("vp9,hw")]
    [InlineData("av1,bpf=400000")]
    [InlineData("h264,hw,bpf=1")]
    [InlineData("h264,bpf=7.5e5")]
    [InlineData("h264,bpf=1e5")]
    [InlineData("h264,bpf=1.5")]
    [InlineData("h264,bpf=99999999999")]
    [InlineData("h264,bpf=400000,hw")]
    [InlineData("h264,hwenc")]
    [InlineData("vp9,swenc")]
    [InlineData("av1,hw,hwenc,bpf=7.5e5")]
    [InlineData("h264,hwdec")]
    [InlineData("vp9,swdec")]
    [InlineData("h264,hw,swenc,hwdec,bpf=7.5e5")]
    public void A_codec_takes_hw_and_bpf_in_either_order(string value)
    {
        Assert.True(VideoChoice.IsValid(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("h265")]
    [InlineData("none,hw")]
    [InlineData("none,bpf=400000")]
    [InlineData("h264,zz")]
    [InlineData("h264,hw,hw")]
    [InlineData("h264,bpf=400000,bpf=1")]
    [InlineData("h264,bpf=")]
    [InlineData("h264,bpf=0")]
    [InlineData("h264,bpf=-1")]
    [InlineData("h264,bpf= 400000")]
    [InlineData("h264,bpf=1e")]
    [InlineData("h264,bpf=e5")]
    [InlineData("h264,bpf=nan")]
    [InlineData("h264,bpf=inf")]
    [InlineData("h264,bpf=1e400")]
    [InlineData("none,hwenc")]
    [InlineData("h264,hwenc,swenc")]
    [InlineData("h264,hwenc,hwenc")]
    [InlineData("h264,hwdec,swdec")]
    [InlineData("h264,swdec,swdec")]
    [InlineData("none,hwdec")]
    [InlineData("h264,sw")]
    [InlineData("h264,enc")]
    public void Anything_else_is_refused(string? value)
    {
        Assert.False(VideoChoice.IsValid(value));
    }

    [Fact]
    public void Hw_stays_here_and_the_encoder_settings_travel()
    {
        Assert.True(VideoChoice.DecodesOnGpu("h264,hw,bpf=400000"));
        Assert.Equal("bpf=400000", VideoChoice.RemoteSetting("h264,hw,bpf=400000"));

        Assert.False(VideoChoice.DecodesOnGpu("h264,bpf=400000"));
        Assert.Null(VideoChoice.RemoteSetting("h264,hw"));

        Assert.Equal("hwenc,bpf=7.5e5", VideoChoice.RemoteSetting("h264,hw,hwenc,bpf=7.5e5"));
        Assert.Equal(
            "swenc,hwdec,bpf=7.5e5",
            VideoChoice.RemoteSetting("h264,hw,swenc,hwdec,bpf=7.5e5"));
        Assert.Equal("bpf=7.5e5,swenc", VideoChoice.RemoteSetting("h264,bpf=7.5e5,swenc"));
        Assert.True(VideoChoice.DecodesOnGpu("h264,hw,hwenc"));
    }
}
