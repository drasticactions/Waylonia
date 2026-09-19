namespace Waylonia.Sessions;

internal interface ISshLink : IAsyncDisposable
{
    string Destination { get; }

    bool IsConnected { get; }

    CancellationToken Lost { get; }

    Task ConnectAsync(CancellationToken cancellation);

    Task<ISshListener> ListenUnixAsync(string remotePath, CancellationToken cancellation);

    Task<ISshCommand> RunAsync(string script, CancellationToken cancellation);
}
