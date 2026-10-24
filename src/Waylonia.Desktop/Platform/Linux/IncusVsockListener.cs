using System.Net.Sockets;
using static Waylonia.WayloniaLog;

namespace Waylonia.Sessions;

[System.Runtime.Versioning.SupportedOSPlatform("linux")]
internal sealed class IncusVsockListener(int fd, uint port, uint cid, string destination) : ISshListener
{
    private const int PollMs = 250;

    private int _disposed;

    private const uint FirstPort = 49152;
    private const uint LastPort = 65535;
    private const int Attempts = 32;

    public const string VhostDevice = "/dev/vhost-vsock";

    public string WaypipeArgument => $"--vsock -s {port}";

    public uint Port => port;

    public static IncusVsockListener Bind(uint cid, string destination)
    {
        SocketException? last = null;
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            var candidate = (uint)Random.Shared.NextInt64(FirstPort, LastPort + 1);
            var inUse = false;
            try
            {
                return new IncusVsockListener(VsockSockets.Listen(candidate, out inUse), candidate, cid, destination);
            }
            catch (SocketException error) when (inUse)
            {
                last = error;
            }
        }

        throw last ?? new SocketException((int)SocketError.AddressAlreadyInUse);
    }

    public ValueTask<Stream?> AcceptAsync(CancellationToken cancellation) =>
        new(Task.Run(() => Accept(cancellation), CancellationToken.None));

    private Stream? Accept(CancellationToken cancellation)
    {
        while (Volatile.Read(ref _disposed) == 0 && !cancellation.IsCancellationRequested)
        {
            var accepted = VsockSockets.Accept(fd, PollMs);
            if (accepted < 0)
            {
                continue;
            }

            var peer = VsockSockets.PeerCid(accepted);
            if (peer == cid)
            {
                return new VsockStream(accepted);
            }

            Log.Warn($"{destination}: closed a vsock connection from CID {peer}, the instance is CID {cid}");
            VsockSockets.Close(accepted);
        }

        return null;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            Task.Delay(PollMs * 2).ContinueWith(_ => VsockSockets.Close(fd), TaskScheduler.Default);
        }
    }
}
