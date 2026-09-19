namespace Waylonia.Sessions;

internal sealed class SshLinkException : Exception
{
    public const string KeyboardInteractive = "keyboard-interactive";

    public SshLinkException(SshLinkReason reason, string message, Exception? inner = null)
        : base(message, inner)
    {
        Reason = reason;
    }

    public SshLinkReason Reason { get; }

    public const string AgentHint = "an encrypted key named in ~/.ssh/config needs an agent";

    public const string ImportHint = "import the key in the session manager";

    public static string Sentence(SshLinkReason reason, string destination, string? detail = null, bool configHint = false, string? hint = null)
    {
        ArgumentNullException.ThrowIfNull(destination);
        var host = SshPromptText.HostOf(destination);
        return reason switch
        {
            SshLinkReason.Unreachable => detail is { Length: > 0 }
                ? $"{host} could not be reached: {Lowercase(detail)}"
                : $"{host} could not be reached",
            SshLinkReason.HostKeyRejected => $"the host key of {host} was not accepted",
            SshLinkReason.HostKeyChanged =>
                $"the host key of {host} changed; remove the old one from {detail ?? "~/.ssh/known_hosts"} if that is expected",
            SshLinkReason.AuthenticationFailed => Authentication(destination, detail, configHint ? hint ?? AgentHint : null),
            SshLinkReason.Cancelled => $"the login to {destination} was cancelled",
            SshLinkReason.ConfigUnsupported => detail is { Length: > 0 } keyword
                ? $"the ssh config for {host} uses {keyword}, which waylonia cannot honour" +
                  (keyword.Equals("ProxyCommand", StringComparison.OrdinalIgnoreCase) ? "; use ProxyJump" : string.Empty)
                : $"the ssh config for {host} uses a keyword waylonia cannot honour",
            SshLinkReason.ForwardRefused =>
                $"the remote side refused to listen on {detail}; sshd needs StreamLocalBindUnlink yes or the socket is stale",
            SshLinkReason.Lost => $"the connection to {destination} ended",
            _ => $"the connection to {destination} failed",
        };
    }

    public static string? KeywordOf(string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var keyword = message.IndexOf("keyword", StringComparison.OrdinalIgnoreCase);
        if (keyword < 0)
        {
            return null;
        }

        var open = message.IndexOf('\'', keyword);
        if (open < 0)
        {
            return null;
        }

        var close = message.IndexOf('\'', open + 1);
        return close > open + 1 ? message[(open + 1)..close] : null;
    }

    private static string Authentication(string destination, string? methods, string? hint)
    {
        var offered = (methods ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var text = $"{destination} refused every credential";
        if (offered.Length == 1 && offered[0] == KeyboardInteractive)
        {
            text += $"; the server offers {KeyboardInteractive}, which waylonia cannot do";
        }
        else if (offered.Length > 0)
        {
            text += $" ({string.Join(", ", offered)})";
        }

        if (hint is not null)
        {
            text += $"; {hint}";
        }

        return text;
    }

    private static string Lowercase(string text) =>
        text.Length > 0 && char.IsUpper(text[0]) && !(text.Length > 1 && char.IsUpper(text[1]))
            ? char.ToLowerInvariant(text[0]) + text[1..].TrimEnd('.')
            : text.TrimEnd('.');
}
