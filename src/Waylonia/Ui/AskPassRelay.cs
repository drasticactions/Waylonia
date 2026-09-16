using System.Net.Sockets;
using System.Text;

namespace Waylonia.Ui;

internal static class AskPassRelay
{
    public const string SocketVariable = "WAYLONIA_ASKPASS_SOCKET";

    public static string Encode(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text));

    public static string Decode(string line) => Encoding.UTF8.GetString(Convert.FromBase64String(line.Trim()));

    public static string EncodeAnswer(string? answer) => answer is null ? "0:" : $"1:{Encode(answer)}";

    public static bool TryDecodeAnswer(string? line, out string? answer)
    {
        answer = null;
        if (line is null)
        {
            return false;
        }

        if (line.StartsWith("0:", StringComparison.Ordinal))
        {
            return true;
        }

        if (line.StartsWith("1:", StringComparison.Ordinal))
        {
            answer = Decode(line[2..]);
            return true;
        }

        return false;
    }

    public static string? Ask(string socketPath, string prompt, TimeSpan timeout, out bool relayed)
    {
        ArgumentException.ThrowIfNullOrEmpty(socketPath);
        ArgumentNullException.ThrowIfNull(prompt);
        relayed = false;
        try
        {
            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            socket.Connect(new UnixDomainSocketEndPoint(socketPath));
            using var stream = new NetworkStream(socket, ownsSocket: false);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
            using var reader = new StreamReader(stream, Encoding.UTF8);
            writer.Write(Encode(prompt));
            writer.Write('\n');
            socket.ReceiveTimeout = (int)timeout.TotalMilliseconds;
            var line = reader.ReadLine();
            if (!TryDecodeAnswer(line, out var answer))
            {
                return null;
            }

            relayed = true;
            return answer;
        }
        catch (Exception error) when (error is SocketException or IOException or FormatException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
