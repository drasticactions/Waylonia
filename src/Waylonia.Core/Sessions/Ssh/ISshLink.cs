namespace Waylonia.Sessions;

internal interface ISshLink : IAsyncDisposable
{
    string Destination { get; }

    bool IsConnected { get; }

    CancellationToken Lost { get; }

    ChannelShape Shape => ChannelShape.Waypipe;

    string? Notice => null;

    Task ConnectAsync(CancellationToken cancellation);

    Task<ISshListener> ListenAsync(string remotePath, CancellationToken cancellation);

    Task<ISessionDisplay> OpenDisplayAsync(DisplayRequest request, CancellationToken cancellation) =>
        throw new NotSupportedException($"The link to {Destination} carries waypipe channels, not a display socket.");

    Task<ISshCommand> RunAsync(string script, CancellationToken cancellation);
}
