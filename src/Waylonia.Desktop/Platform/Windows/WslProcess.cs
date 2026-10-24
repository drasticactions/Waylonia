using System.Diagnostics;
using System.Text;
using static Waylonia.WayloniaLog;

namespace Waylonia.Sessions;

internal static class WslProcess
{
    public static Process Start(IReadOnlyList<string> arguments, bool input)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var info = new ProcessStartInfo("wsl.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = input,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = input ? null : Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        info.Environment["WSL_UTF8"] = "1";
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        return Process.Start(info) ?? throw new InvalidOperationException("The wsl.exe program did not start.");
    }

    public static async Task<(int Code, string Output, string Error)> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellation)
    {
        using var process = Start(arguments, input: false);
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
            Log.Debug(error, $"wsl: process already gone");
        }
    }

    public static string LastLine(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var lines = text.Replace("\0", string.Empty, StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return lines.Length > 0 ? lines[^1] : string.Empty;
    }
}
