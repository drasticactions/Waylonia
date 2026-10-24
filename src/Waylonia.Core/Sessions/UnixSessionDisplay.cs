using System.Net.Sockets;
using System.Runtime.Versioning;

namespace Waylonia.Sessions;

[UnsupportedOSPlatform("browser")]
[UnsupportedOSPlatform("windows")]
internal sealed class UnixSessionDisplay : ISessionDisplay
{
    private readonly Socket _socket;
    private readonly string _path;
    private readonly Action? _cleanup;
    private bool _disposed;

    private UnixSessionDisplay(Socket socket, string path, string clientDisplay, string? clientXDisplay, Action? cleanup)
    {
        _socket = socket;
        _path = path;
        _cleanup = cleanup;
        ClientDisplay = clientDisplay;
        ClientXDisplay = clientXDisplay;
    }

    public string ClientDisplay { get; }

    public string? ClientXDisplay { get; }

    public string Path => _path;

    public int ListenFd => (int)_socket.SafeHandle.DangerousGetHandle();

    public static UnixSessionDisplay Bind(
        string path, UnixFileMode mode, string clientDisplay, string? clientXDisplay, Action? cleanup = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(clientDisplay);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            socket.Bind(new UnixDomainSocketEndPoint(path));
            File.SetUnixFileMode(path, mode);

            socket.Listen(16);
        }
        catch
        {
            socket.Dispose();
            throw;
        }

        return new UnixSessionDisplay(socket, path, clientDisplay, clientXDisplay, cleanup);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _socket.Dispose();
        try
        {
            File.Delete(_path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            WayloniaLog.Log.Debug(error, $"session display: removing {_path} failed");
        }

        _cleanup?.Invoke();
    }
}
