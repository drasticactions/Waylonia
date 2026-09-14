using System.Runtime.InteropServices;
using System.Text;

namespace Waylonia;

internal static class SshVis
{
    private static readonly Encoding Native = NativeEncoding();

    public static string Unescape(string line)
    {
        if (line.IndexOf('\\') < 0)
        {
            return line;
        }

        var bytes = new List<byte>(line.Length);
        var index = 0;
        while (index < line.Length)
        {
            if (Octal(line, index) is { } value)
            {
                bytes.Add(value);
                index += 4;
                continue;
            }

            var character = line[index];
            if (character < 0x80)
            {
                bytes.Add((byte)character);
            }
            else
            {
                bytes.AddRange(Encoding.UTF8.GetBytes(character.ToString()));
            }

            index++;
        }

        return Decode([.. bytes]);
    }

    private static byte? Octal(string line, int index)
    {
        if (index + 3 >= line.Length || line[index] != '\\')
        {
            return null;
        }

        var value = 0;
        for (var digit = 1; digit <= 3; digit++)
        {
            var character = line[index + digit];
            if (character is < '0' or > '7')
            {
                return null;
            }

            value = (value * 8) + (character - '0');
        }

        return value > byte.MaxValue ? null : (byte)value;
    }

    private static string Decode(byte[] bytes)
    {
        var strict = new UTF8Encoding(false, true);
        try
        {
            return strict.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Native.GetString(bytes);
        }
    }

    private static Encoding NativeEncoding()
    {
        if (!OperatingSystem.IsWindows())
        {
            return Encoding.UTF8;
        }

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        try
        {
            return Encoding.GetEncoding((int)GetACP());
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException)
        {
            return Encoding.UTF8;
        }
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetACP();
}
