namespace Waylonia.Sessions;

internal interface ISshListener : IDisposable
{
    ValueTask<Stream?> AcceptAsync(CancellationToken cancellation);
}
