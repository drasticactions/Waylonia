namespace Waylonia.Sessions;

internal static class SshPromptText
{
    public static string For(SshSecretPrompt prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        return prompt.Kind switch
        {
            SshSecretKind.Passphrase => $"Enter passphrase for key '{prompt.KeyPath}':",
            _ => $"{prompt.Destination}'s password:",
        };
    }

    public static string For(SshHostKeyPrompt prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        var head = prompt.State == SshHostKeyState.Changed
            ? $"WARNING: the host key of '{prompt.Host} ({prompt.Port})' has changed."
            : $"The authenticity of host '{prompt.Host} ({prompt.Port})' can't be established.";
        return head +
            $"\n{KeyTypeName(prompt.KeyType)} key fingerprint is {prompt.Fingerprint}." +
            "\nAre you sure you want to continue connecting (yes/no)?";
    }

    public static string KeyTypeName(string keyType)
    {
        ArgumentNullException.ThrowIfNull(keyType);
        if (keyType.StartsWith("ecdsa-", StringComparison.Ordinal))
        {
            return "ECDSA";
        }

        if (keyType.StartsWith("sk-", StringComparison.Ordinal))
        {
            return KeyTypeName(keyType[3..]) + "-SK";
        }

        var name = keyType.StartsWith("ssh-", StringComparison.Ordinal) ? keyType[4..] : keyType;
        var at = name.IndexOf('@');
        if (at > 0)
        {
            name = name[..at];
        }

        return name.ToUpperInvariant();
    }

    public static string HostOf(string destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        var host = destination;
        var at = host.LastIndexOf('@');
        if (at >= 0)
        {
            host = host[(at + 1)..];
        }

        if (host.StartsWith('[') && host.IndexOf(']') is var close and > 0)
        {
            host = host[..(close + 1)];
        }
        else if (host.Count(static c => c == ':') == 1)
        {
            host = host[..host.IndexOf(':')];
        }

        return host;
    }
}
