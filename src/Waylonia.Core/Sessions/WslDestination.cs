namespace Waylonia.Sessions;

internal static class WslDestination
{
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

    public static IReadOnlyList<string> Script(string distro, string script)
    {
        ArgumentNullException.ThrowIfNull(script);
        return Arguments(distro, "sh", "-c", PidMarker.Wrapper, "sh", script);
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
            .Where(line => DestinationKinds.Wsl.NameProblem(line) is null)];
    }
}
