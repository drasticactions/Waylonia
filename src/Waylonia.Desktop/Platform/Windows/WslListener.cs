using System.Net.Sockets;
using Basin.Transport.Waypipe;

namespace Waylonia.Sessions;

internal sealed class WslListener(Socket socket, uint port) : ISshListener
{
    private const uint FirstPort = 49152;
    private const uint LastPort = 65535;
    private const int Attempts = 32;

    public string WaypipeArgument => $"--vsock -s {port}";

    public uint Port => port;

    public static WslListener Bind(Guid vmId)
    {
        SocketException? last = null;
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            var candidate = (uint)Random.Shared.NextInt64(FirstPort, LastPort + 1);
            var socket = new Socket(HyperVEndPoint.HyperVFamily, SocketType.Stream, HyperVEndPoint.RawProtocol);
            try
            {
                socket.Bind(HyperVEndPoint.ForVsockPort(vmId, candidate));
                socket.Listen(8);
                return new WslListener(socket, candidate);
            }
            catch (SocketException error) when (error.SocketErrorCode == SocketError.AddressAlreadyInUse)
            {
                last = error;
                socket.Dispose();
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        throw last ?? new SocketException((int)SocketError.AddressAlreadyInUse);
    }

    public async ValueTask<Stream?> AcceptAsync(CancellationToken cancellation)
    {
        try
        {
            var accepted = await socket.AcceptAsync(cancellation).ConfigureAwait(false);
            return new NetworkStream(accepted, ownsSocket: true);
        }
        catch (Exception error) when (error is SocketException or ObjectDisposedException or OperationCanceledException)
        {
            return null;
        }
    }

    public void Dispose() => socket.Dispose();
}
