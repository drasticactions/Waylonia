using System.Diagnostics;
using System.Text;
using static Waylonia.WayloniaLog;

namespace Waylonia.Sessions;

[System.Runtime.Versioning.SupportedOSPlatform("linux")]
internal static class CliProcess
{
    public static bool OnPath(string program)
    {
        ArgumentNullException.ThrowIfNull(program);
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        return path.Split(':', StringSplitOptions.RemoveEmptyEntries)
            .Any(directory => File.Exists(Path.Combine(directory, program)));
    }

    public static Process Start(
        string program, IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string>? environment, bool input)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(arguments);
        var info = new ProcessStartInfo(program)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = input,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = input ? null : Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        if (environment is not null)
        {
            info.Environment.Clear();
            foreach (var (key, value) in environment)
            {
                info.Environment[key] = value;
            }
        }

        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        return Process.Start(info) ?? throw new InvalidOperationException($"The program {program} did not start.");
    }

    public static async Task<(int Code, string Output, string Error)> RunAsync(
        string program, IReadOnlyList<string> arguments, CancellationToken cancellation, IReadOnlyDictionary<string, string>? environment = null)
    {
        using var process = Start(program, arguments, environment, input: false);
        try
        {
            var output = process.StandardOutput.ReadToEndAsync(cancellation);
            var error = process.StandardError.ReadToEndAsync(cancellation);
            await process.WaitForExitAsync(cancellation).ConfigureAwait(false);
            return (process.ExitCode, await output.ConfigureAwait(false), await error.ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            throw;
        }
    }

    public static void Kill(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            Log.Debug(error, $"command: process already gone");
        }
    }

    public static string LastLine(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var lines = text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return lines.Length > 0 ? lines[^1] : string.Empty;
    }

    public static IReadOnlyDictionary<string, string> HostEnvironment()
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key && entry.Value is string value)
            {
                variables[key] = value;
            }
        }

        return variables;
    }
}
