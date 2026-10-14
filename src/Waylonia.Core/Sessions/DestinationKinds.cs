using System.Buffers;

namespace Waylonia.Sessions;

internal static class DestinationKinds
{
    private static readonly SearchValues<char> WslCharacters =
        SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789._-");

    private static readonly SearchValues<char> PodmanCharacters =
        SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789._-");

    private static readonly SearchValues<char> IncusCharacters =
        SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-");

    public const int IncusNameLimit = 63;

    public static DestinationKind Ssh { get; } = new()
    {
        Key = "ssh",
        Label = "ssh",
        Noun = "an ssh destination",
        Verb = "ssh logs in to a host",
        Caption = "ssh",
        HostOs = "any host",
        Placeholder = "user@host",
        UserNote = "an ssh destination names it as user@host",
        NameRule = static _ => null,
    };

    public static DestinationKind Wsl { get; } = new()
    {
        Key = "wsl",
        Label = "WSL",
        Noun = "a WSL distribution",
        Verb = "wsl starts a WSL distribution",
        Caption = "distribution",
        HostOs = "Windows",
        Placeholder = "the default distribution",
        FormHint = "Use the distribution name wsl -l shows, or leave it empty for the default one.",
        UserNote = "wsl runs as the distribution's default user",
        AllowsEmptyName = true,
        NameRule = static name => name.AsSpan().ContainsAnyExcept(WslCharacters)
            ? $"'{name}' is not a WSL distribution name; one is letters, digits, '.', '_' and '-'"
            : null,
    };

    public static DestinationKind Distrobox { get; } = Podman("distrobox");

    public static DestinationKind Toolbox { get; } = Podman("toolbox");

    public static DestinationKind Incus { get; } = new()
    {
        Key = "incus",
        Label = "Incus",
        Noun = "an Incus instance",
        Verb = "incus enters a local Incus instance",
        Caption = "instance",
        HostOs = "Linux",
        Placeholder = "instance name",
        FormHint = "Use the instance name incus list shows.",
        TakesUser = true,
        NameRule = static name =>
            name.Length > IncusNameLimit || !char.IsAsciiLetter(name[0]) || name.AsSpan().ContainsAnyExcept(IncusCharacters)
                ? $"'{name}' is not an Incus instance name; one starts with a letter, holds letters, digits and '-', and has at most {IncusNameLimit} characters"
                : null,
    };

    public static IReadOnlyList<DestinationKind> All { get; } = [Ssh, Wsl, Distrobox, Toolbox, Incus];

    public static IReadOnlyList<DestinationKind> Targets { get; } = [Wsl, Distrobox, Toolbox, Incus];

    public static DestinationKind For(string destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        foreach (var kind in Targets)
        {
            if (kind.TryParse(destination, out _))
            {
                return kind;
            }
        }

        return Ssh;
    }

    public static DestinationKind? ForKey(string key) => All.FirstOrDefault(kind => kind.Key == key);

    public static string Name(string destination)
    {
        var kind = For(destination);
        return kind.TryParse(destination, out var name) ? name : destination;
    }

    public static string BothProblem(DestinationKind first, DestinationKind second) =>
        $"{first.Verb} and {second.Verb}; a session takes one of them";

    private static DestinationKind Podman(string key) => new()
    {
        Key = key,
        Label = key,
        Noun = $"a {key} container",
        Verb = $"{key} enters a local container",
        Caption = "container",
        HostOs = "Linux",
        Placeholder = "container name",
        FormHint = $"Use the container name {key} list shows.",
        UserNote = "distrobox and toolbox run as you",
        SharesHome = true,
        SlowFirstStart = true,
        DesktopProblem =
            $"a {key} container shares this machine's session bus and runtime directory, so a desktop session in it would fight the one you are in; use an Incus instance",
        NameRule = static name =>
            !char.IsAsciiLetterOrDigit(name[0]) || name.AsSpan().ContainsAnyExcept(PodmanCharacters)
                ? $"'{name}' is not a container name; one starts with a letter or digit and goes on with letters, digits, '_', '.' and '-'"
                : null,
    };
}
