namespace Waylonia.Sessions;

internal interface ISshListener : IDisposable
{
    string WaypipeArgument { get; }

    ValueTask<Stream?> AcceptAsync(CancellationToken cancellation);
}
