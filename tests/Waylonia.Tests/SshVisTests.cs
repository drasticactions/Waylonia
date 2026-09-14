using Waylonia;
using Xunit;

namespace Waylonia.Tests;

public sealed class SshVisTests
{
    [Theory]
    [InlineData("""plain ascii line, no escapes""")]
    [InlineData("a trailing backslash \\")]
    [InlineData("""not octal: \999 and \12""")]
    [InlineData("")]
    public void Text_without_complete_escapes_is_unchanged(string line) =>
        Assert.Equal(line, SshVis.Unescape(line));

    [Fact]
    public void Escaped_utf8_becomes_the_character_it_encodes() =>
        Assert.Equal("eclair \u00e9", SshVis.Unescape("""eclair \303\251"""));

    [Fact]
    public void Ascii_around_an_escape_survives() =>
        Assert.Equal("a\u00e9b", SshVis.Unescape("""a\303\251b"""));

    [Fact]
    public void A_literal_byte_continues_a_multi_byte_sequence()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "the fallback is the Windows ANSI codepage");
        Assert.SkipUnless(GetACP() == 932, "the message is encoded in CP932");
        var decoded = SshVis.Unescape(
            """ssh: Could not resolve hostname mind: """ +
            """\202\273\202\314\202\346\202\244\202\310\203z\203X\203g""" +
            """\202\315\225s\226\276\202\305\202\267\201B""");
        Assert.Equal(
            "ssh: Could not resolve hostname mind: " +
            "\u305d\u306e\u3088\u3046\u306a\u30db\u30b9\u30c8\u306f\u4e0d\u660e\u3067\u3059\u3002",
            decoded);
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern uint GetACP();
}
