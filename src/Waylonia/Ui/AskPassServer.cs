using System.Net.Sockets;
using System.Text;
using Basin.Diagnostics;

namespace Waylonia.Ui;

internal sealed class AskPassServer : IDisposable
{
    private readonly Socket _listener;
    private readonly Func<string, Task<string?>> _ask;
    private readonly BasinLogger _log;
    private bool _disposed;

    private AskPassServer(Socket listener, string path, Func<string, Task<string?>> ask, BasinLogger log)
    {
        _listener = listener;
        Path = path;
        _ask = ask;
        _log = log;
        _ = AcceptAsync();
    }

    public string Path { get; }

    public static string DefaultPath()
    {
        var runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        var directory = !string.IsNullOrEmpty(runtime) && Directory.Exists(runtime) ? runtime : System.IO.Path.GetTempPath();
        return System.IO.Path.Combine(directory, $"waylonia-askpass-{Environment.ProcessId}");
    }

    public static AskPassServer? TryStart(string path, Func<string, Task<string?>> ask, BasinLogger log)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(ask);
        try
        {
            File.Delete(path);
            var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            listener.Bind(new UnixDomainSocketEndPoint(path));
            listener.Listen(4);
            return new AskPassServer(listener, path, ask, log);
        }
        catch (Exception error) when (error is SocketException or IOException or UnauthorizedAccessException)
        {
            log.Warn($"ssh prompts open in their own window: the askpass socket {path} could not be bound: {error.Message}");
            return null;
        }
    }

    private async Task AcceptAsync()
    {
        while (!_disposed)
        {
            Socket client;
            try
            {
                client = await _listener.AcceptAsync().ConfigureAwait(false);
            }
            catch (Exception error) when (error is SocketException or ObjectDisposedException)
            {
                return;
            }

            _ = ServeAsync(client);
        }
    }

    private async Task ServeAsync(Socket client)
    {
        using var socket = client;
        try
        {
            using var stream = new NetworkStream(socket, ownsSocket: false);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
            var line = await reader.ReadLineAsync().ConfigureAwait(false);
            if (line is null)
            {
                return;
            }

            var prompt = AskPassRelay.Decode(line);
            var answer = await _ask(prompt).ConfigureAwait(false);
            await writer.WriteAsync(AskPassRelay.EncodeAnswer(answer) + "\n").ConfigureAwait(false);
        }
        catch (Exception error) when (error is SocketException or IOException or FormatException)
        {
            _log.Warn($"an ssh prompt could not be relayed: {error.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _listener.Dispose();
        try
        {
            File.Delete(Path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
    }
}
