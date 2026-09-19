namespace Waylonia.Sessions;

internal enum SshLinkReason
{
    Unreachable,
    HostKeyRejected,
    HostKeyChanged,
    AuthenticationFailed,
    Cancelled,
    ConfigUnsupported,
    ForwardRefused,
    Lost,
}
