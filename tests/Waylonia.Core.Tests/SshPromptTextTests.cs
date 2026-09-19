using Waylonia.Sessions;
using Xunit;

namespace Waylonia.Tests;

public sealed class SshPromptTextTests
{
    [Fact]
    public void A_password_prompt_reads_like_openssh()
    {
        Assert.Equal("da@devbox's password:", SshPromptText.For(new SshSecretPrompt(SshSecretKind.Password, "da@devbox", 1)));
    }

    [Fact]
    public void A_passphrase_prompt_names_the_key()
    {
        Assert.Equal(
            "Enter passphrase for key '/home/u/.ssh/id_ed25519':",
            SshPromptText.For(new SshSecretPrompt(SshSecretKind.Passphrase, "da@devbox", 1, "/home/u/.ssh/id_ed25519")));
    }

    [Fact]
    public void An_unknown_host_key_asks_the_openssh_question()
    {
        var text = SshPromptText.For(new SshHostKeyPrompt("devbox", 22, "ssh-ed25519", "SHA256:abc", SshHostKeyState.Unknown));
        Assert.Equal(
            "The authenticity of host 'devbox (22)' can't be established.\n" +
            "ED25519 key fingerprint is SHA256:abc.\n" +
            "Are you sure you want to continue connecting (yes/no)?",
            text);
    }

    [Fact]
    public void A_changed_host_key_warns_first()
    {
        var text = SshPromptText.For(new SshHostKeyPrompt("devbox", 2222, "ecdsa-sha2-nistp256", "SHA256:abc", SshHostKeyState.Changed));
        Assert.StartsWith("WARNING: the host key of 'devbox (2222)' has changed.\nECDSA key fingerprint", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ssh-ed25519", "ED25519")]
    [InlineData("ssh-rsa", "RSA")]
    [InlineData("ecdsa-sha2-nistp521", "ECDSA")]
    [InlineData("sk-ssh-ed25519@openssh.com", "ED25519-SK")]
    public void Key_types_are_spelled_as_openssh_prints_them(string type, string name)
    {
        Assert.Equal(name, SshPromptText.KeyTypeName(type));
    }

    [Theory]
    [InlineData("user@devbox", "devbox")]
    [InlineData("devbox", "devbox")]
    [InlineData("user@devbox:2222", "devbox")]
    [InlineData("user@[fe80::1]:22", "[fe80::1]")]
    public void The_host_is_cut_out_of_a_destination(string destination, string host)
    {
        Assert.Equal(host, SshPromptText.HostOf(destination));
    }
}
