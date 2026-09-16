using Tmds.Ssh;

namespace Waylonia.Sessions;

internal sealed class TmdsSshListener(RemoteListener listener) : ISshListener
{
    public async ValueTask<Stream?> AcceptAsync(CancellationToken cancellation)
    {
        try
        {
            var connection = await listener.AcceptAsync(cancellation).ConfigureAwait(false);
            return connection.HasStream ? connection.MoveStream() : null;
        }
        catch (Exception error) when (error is SshConnectionClosedException or ObjectDisposedException or SshException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        try
        {
            listener.Stop();
        }
        catch (Exception error) when (error is ObjectDisposedException or SshException)
        {
        }

        listener.Dispose();
    }
}
