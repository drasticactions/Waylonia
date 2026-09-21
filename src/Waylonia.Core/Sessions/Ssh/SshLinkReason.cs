namespace Waylonia.Sessions;

internal enum SshLinkReason
{
    Unreachable,
    HostKeyRejected,
    HostKeyChanged,
    AuthenticationFailed,
    Canceled,
    ConfigUnsupported,
    ForwardRefused,
    Lost,
}
