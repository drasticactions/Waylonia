using Waylonia.Sessions;
using Xunit;

namespace Waylonia.Tests;

public sealed class SshLinkExceptionTests
{
    [Theory]
    [InlineData("Unreachable", "user@devbox", "Connection refused", false, "devbox", "Connection refused.")]
    [InlineData("Unreachable", "devbox:2222", null, false, "devbox", null)]
    [InlineData("HostKeyRejected", "user@devbox", null, false, "devbox", null)]
    [InlineData("HostKeyChanged", "user@devbox", null, false, "devbox", "~/.ssh/known_hosts")]
    [InlineData("HostKeyChanged", "user@devbox", "/data/Documents/ssh/known_hosts", false, "devbox", "/data/Documents/ssh/known_hosts")]
    [InlineData("AuthenticationFailed", "user@devbox", null, false, "user@devbox", null)]
    [InlineData("AuthenticationFailed", "user@devbox", "password,publickey", false, "user@devbox", "(password, publickey)")]
    [InlineData("AuthenticationFailed", "user@devbox", "keyboard-interactive", false, "user@devbox", "keyboard-interactive")]
    [InlineData("AuthenticationFailed", "user@devbox", "publickey", true, "user@devbox", SshLinkException.AgentHint)]
    [InlineData("Canceled", "user@devbox", null, false, "user@devbox", null)]
    [InlineData("ConfigUnsupported", "user@devbox", "ProxyCommand", false, "devbox", "ProxyJump")]
    [InlineData("ConfigUnsupported", "devbox", "SetEnv", false, "devbox", "SetEnv")]
    [InlineData("ForwardRefused", "user@devbox", "/tmp/waylonia-1-1.sock", false, "devbox", "StreamLocalBindUnlink")]
    [InlineData("Lost", "user@devbox", null, false, "user@devbox", null)]
    public void The_sentence_names_the_host_and_the_detail_in_a_capitalized_sentence(
        string reason, string destination, string? detail, bool hint, string host, string? fragment)
    {
        var sentence = SshLinkException.Sentence(Enum.Parse<SshLinkReason>(reason), destination, detail, hint);

        Assert.Contains(host, sentence, StringComparison.Ordinal);
        if (fragment is not null)
        {
            Assert.Contains(fragment, sentence, StringComparison.Ordinal);
        }

        Assert.EndsWith(".", sentence, StringComparison.Ordinal);
        Assert.DoesNotContain(";", sentence, StringComparison.Ordinal);
    }

    [Fact]
    public void A_host_that_imports_keys_hints_at_the_import_instead_of_an_agent()
    {
        Assert.EndsWith(
            SshLinkException.ImportHint,
            SshLinkException.Sentence(SshLinkReason.AuthenticationFailed, "user@devbox", "publickey", true, SshLinkException.ImportHint),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            SshLinkException.ImportHint,
            SshLinkException.Sentence(SshLinkReason.AuthenticationFailed, "user@devbox", "publickey", false, SshLinkException.ImportHint),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Unsupported keyword: 'ProxyCommand' (value: 'nc %h %p').", "ProxyCommand")]
    [InlineData("Unsupported value 'ask' for keyword 'UpdateHostKeys'.", "UpdateHostKeys")]
    [InlineData("Something else entirely", null)]
    public void The_keyword_is_taken_from_the_parser_message(string message, string? keyword)
    {
        Assert.Equal(keyword, SshLinkException.KeywordOf(message));
    }

    [Fact]
    public void The_exception_carries_its_reason_and_sentence()
    {
        var error = new SshLinkException(SshLinkReason.Lost, SshLinkException.Sentence(SshLinkReason.Lost, "dev"));
        Assert.Equal(SshLinkReason.Lost, error.Reason);
        Assert.Contains("dev", error.Message, StringComparison.Ordinal);
    }
}
