using Waylonia.Sessions;
using Xunit;

namespace Waylonia.Tests;

public sealed class SshLinkExceptionTests
{
    [Theory]
    [InlineData("Unreachable", "user@devbox", "Connection refused.", false, "devbox could not be reached: connection refused")]
    [InlineData("Unreachable", "devbox:2222", null, false, "devbox could not be reached")]
    [InlineData("HostKeyRejected", "user@devbox", null, false, "the host key of devbox was not accepted")]
    [InlineData("HostKeyChanged", "user@devbox", null, false, "the host key of devbox changed; remove the old one from ~/.ssh/known_hosts if that is expected")]
    [InlineData("AuthenticationFailed", "user@devbox", null, false, "user@devbox refused every credential")]
    [InlineData("AuthenticationFailed", "user@devbox", "password,publickey", false, "user@devbox refused every credential (password, publickey)")]
    [InlineData("AuthenticationFailed", "user@devbox", "keyboard-interactive", false, "user@devbox refused every credential; the server offers keyboard-interactive, which waylonia cannot do")]
    [InlineData("AuthenticationFailed", "user@devbox", "publickey", true, "user@devbox refused every credential (publickey); an encrypted key named in ~/.ssh/config needs an agent")]
    [InlineData("Cancelled", "user@devbox", null, false, "the login to user@devbox was cancelled")]
    [InlineData("ConfigUnsupported", "user@devbox", "ProxyCommand", false, "the ssh config for devbox uses ProxyCommand, which waylonia cannot honour; use ProxyJump")]
    [InlineData("ConfigUnsupported", "devbox", "SetEnv", false, "the ssh config for devbox uses SetEnv, which waylonia cannot honour")]
    [InlineData("ForwardRefused", "user@devbox", "/tmp/waylonia-1-1.sock", false, "the remote side refused to listen on /tmp/waylonia-1-1.sock; sshd needs StreamLocalBindUnlink yes or the socket is stale")]
    [InlineData("Lost", "user@devbox", null, false, "the connection to user@devbox ended")]
    public void The_sentence_table_speaks_in_lowercase_user_terms(string reason, string destination, string? detail, bool hint, string expected)
    {
        Assert.Equal(expected, SshLinkException.Sentence(Enum.Parse<SshLinkReason>(reason), destination, detail, hint));
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
        Assert.Equal("the connection to dev ended", error.Message);
    }
}
