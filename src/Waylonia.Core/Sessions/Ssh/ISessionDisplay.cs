namespace Waylonia.Sessions;

internal interface ISessionDisplay : IDisposable
{
    string ClientDisplay { get; }

    string? ClientXDisplay { get; }

    int ListenFd { get; }
}
