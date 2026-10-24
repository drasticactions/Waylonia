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

    public const string AgentHint = "To use an encrypted key that ~/.ssh/config names, add it to an ssh agent.";

    public const string ImportHint = "Import your key in the session manager.";

    public static string Sentence(SshLinkReason reason, string destination, string? detail = null, bool configHint = false, string? hint = null)
    {
        ArgumentNullException.ThrowIfNull(destination);
        var host = SshPromptText.HostOf(destination);
        return reason switch
        {
            SshLinkReason.Unreachable => detail is { Length: > 0 }
                ? $"Couldn't reach {host}. {UiSentence.Of(detail)}"
                : $"Couldn't reach {host}.",
            SshLinkReason.HostKeyRejected => $"The host key of {host} wasn't accepted, so Waylonia didn't connect.",
            SshLinkReason.HostKeyChanged =>
                $"The host key of {host} has changed. If you expected that, remove the old key from {detail ?? "~/.ssh/known_hosts"} and connect again.",
            SshLinkReason.AuthenticationFailed => Authentication(destination, detail, configHint ? hint ?? AgentHint : null),
            SshLinkReason.Canceled => $"The login to {destination} was canceled.",
            SshLinkReason.ConfigUnsupported => detail is { Length: > 0 } keyword
                ? $"Your ssh config for {host} uses {keyword}, which Waylonia doesn't support." +
                  (keyword.Equals("ProxyCommand", StringComparison.OrdinalIgnoreCase) ? " Use ProxyJump instead." : string.Empty)
                : $"Your ssh config for {host} uses a keyword that Waylonia doesn't support.",
            SshLinkReason.ForwardRefused =>
                $"{host} refused to listen on {detail}. A socket from an earlier run may be in the way, so set StreamLocalBindUnlink yes in its sshd_config.",
            SshLinkReason.Lost => $"The connection to {destination} ended.",
            _ => $"The connection to {destination} failed.",
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
        var text = $"{destination} didn't accept any of your credentials";
        if (offered.Length == 1 && offered[0] == KeyboardInteractive)
        {
            text += $". It only offers {KeyboardInteractive} login, which Waylonia doesn't support.";
        }
        else if (offered.Length > 0)
        {
            text += $" ({string.Join(", ", offered)}).";
        }
        else
        {
            text += ".";
        }

        if (hint is not null)
        {
            text += $" {hint}";
        }

        return text;
    }
}
