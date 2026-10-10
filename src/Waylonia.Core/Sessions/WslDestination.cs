using System.Buffers;

namespace Waylonia.Sessions;

internal static class WslDestination
{
    public const string Prefix = "wsl:";

    private static readonly SearchValues<char> DistroCharacters =
        SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789._-");

    public static string For(string distro)
    {
        ArgumentNullException.ThrowIfNull(distro);
        return Prefix + distro;
    }

    public static bool TryParse(string destination, out string distro)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (destination.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            distro = destination[Prefix.Length..];
            return true;
        }

        distro = string.Empty;
        return false;
    }

    public static bool IsWsl(string destination) => TryParse(destination, out _);

    public static string? DistroProblem(string distro)
    {
        ArgumentNullException.ThrowIfNull(distro);
        return distro.AsSpan().ContainsAnyExcept(DistroCharacters)
            ? $"'{distro}' is not a WSL distribution name; one is letters, digits, '.', '_' and '-'"
            : null;
    }

    public static IReadOnlyList<string> Arguments(string distro, params IReadOnlyList<string> command)
    {
        ArgumentNullException.ThrowIfNull(distro);
        ArgumentNullException.ThrowIfNull(command);
        var arguments = new List<string>();
        if (distro.Length > 0)
        {
            arguments.Add("-d");
            arguments.Add(distro);
        }

        arguments.Add("--cd");
        arguments.Add("~");
        arguments.Add("--exec");
        arguments.AddRange(command);
        return arguments;
    }

    public const string PidMarker = "waylonia-wsl-pid ";

    public static IReadOnlyList<string> Script(string distro, string script)
    {
        ArgumentNullException.ThrowIfNull(script);
        return Arguments(distro, "sh", "-c", $"echo \"{PidMarker}$$\" >&2; exec sh -c \"$1\"", "sh", script);
    }

    public static int? ParsePid(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        return line.StartsWith(PidMarker, StringComparison.Ordinal)
            && int.TryParse(line.AsSpan(PidMarker.Length), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var pid)
            ? pid
            : null;
    }

    public static IReadOnlyList<string> Signal(string distro, string name, int pid) =>
        Arguments(distro, "kill", "-s", name, pid.ToString(System.Globalization.CultureInfo.InvariantCulture));

    public static IReadOnlyList<string> VmId(string distro) => Arguments(distro, "wslinfo", "--vm-id");

    public static IReadOnlyList<string> ListDistros() => ["--list", "--quiet"];

    public static IReadOnlyList<string> ParseDistros(string output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return [.. output
            .Replace("\0", string.Empty, StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(line => DistroProblem(line) is null)];
    }
}
