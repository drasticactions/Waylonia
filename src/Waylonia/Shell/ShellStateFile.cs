using System.Globalization;
using Basin.Diagnostics;
using Tomlyn.Model;
using Waylonia.Cli;

namespace Waylonia.Shell;

internal static class ShellStateFile
{
    public static string DefaultPath()
    {
        var stateHome = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
        if (OperatingSystem.IsLinux() && (string.IsNullOrEmpty(stateHome) || !Path.IsPathRooted(stateHome)))
        {
            stateHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "state");
        }
        else if (!OperatingSystem.IsLinux())
        {
            stateHome = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        }

        return Path.Combine(stateHome!, "waylonia", "shell.toml");
    }

    public static ShellWindowState Load(string path, BasinLogger log)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (TomlConfig.Read(path, log) is not { } table)
        {
            return ShellWindowState.Default;
        }

        return Parse(table);
    }

    public static ShellWindowState Parse(TomlTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        var width = Integer(table, "width") ?? ShellWindowState.DefaultWidth;
        var height = Integer(table, "height") ?? ShellWindowState.DefaultHeight;
        if (width < 320 || height < 240)
        {
            width = ShellWindowState.DefaultWidth;
            height = ShellWindowState.DefaultHeight;
        }

        return new ShellWindowState(width, height, Integer(table, "x"), Integer(table, "y"), TomlConfig.Flag(table, "fullscreen", false));
    }

    public static string Render(ShellWindowState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var lines = new List<string>
        {
            $"width = {state.Width.ToString(CultureInfo.InvariantCulture)}",
            $"height = {state.Height.ToString(CultureInfo.InvariantCulture)}",
        };
        if (state.X is { } x && state.Y is { } y)
        {
            lines.Add($"x = {x.ToString(CultureInfo.InvariantCulture)}");
            lines.Add($"y = {y.ToString(CultureInfo.InvariantCulture)}");
        }

        if (state.FullScreen)
        {
            lines.Add("fullscreen = true");
        }

        return string.Join('\n', lines) + "\n";
    }

    public static void Save(string path, ShellWindowState state, BasinLogger log)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path, Render(state));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            log.Warn($"cannot write the shell window state to {path}: {error.Message}");
        }
    }

    private static int? Integer(TomlTable table, string key) =>
        table.TryGetValue(key, out var value) && value is long number && number is >= int.MinValue and <= int.MaxValue
            ? (int)number
            : null;
}
