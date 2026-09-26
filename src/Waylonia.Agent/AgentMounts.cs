using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using static Waylonia.Agent.AgentLog;

namespace Waylonia.Agent;

/// <summary>
/// The mounts that the agent's bus-activated services put inside its runtime directory, such as
/// xdg-document-portal's <c>doc</c> and gvfsd-fuse's <c>gvfs</c>.
/// </summary>
internal static class AgentMounts
{
    private const string MountInfo = "/proc/self/mountinfo";

    [SupportedOSPlatform("linux")]
    public static IReadOnlyList<string> Under(string root)
    {
        try
        {
            return Under(root, File.ReadAllText(MountInfo));
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            Log.Debug($"{MountInfo} cannot be read: {failure.Message}");
            return [];
        }
    }

    public static IReadOnlyList<string> Under(string root, string mountInfo)
    {
        ArgumentException.ThrowIfNullOrEmpty(root);
        ArgumentNullException.ThrowIfNull(mountInfo);
        var trimmed = root.TrimEnd('/');
        var prefix = trimmed + "/";
        var found = new List<string>();
        foreach (var line in mountInfo.Split('\n'))
        {
            var fields = line.Split(' ');
            if (fields.Length < 5)
            {
                continue;
            }

            var point = Unescape(fields[4]);
            if (point == trimmed || point.StartsWith(prefix, StringComparison.Ordinal))
            {
                found.Add(point);
            }
        }

        return found.OrderByDescending(static point => point.Length).ToArray();
    }

    [SupportedOSPlatform("linux")]
    public static bool Detach(string mountPoint)
    {
        ArgumentException.ThrowIfNullOrEmpty(mountPoint);
        foreach (var tool in (ReadOnlySpan<string>)["fusermount3", "fusermount"])
        {
            var start = new ProcessStartInfo(tool)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            start.ArgumentList.Add("-u");
            start.ArgumentList.Add("-z");
            start.ArgumentList.Add(mountPoint);
            try
            {
                using var process = Process.Start(start);
                if (process is null)
                {
                    continue;
                }

                var errors = process.StandardError.ReadToEnd();
                if (!process.WaitForExit(2000))
                {
                    process.Kill();
                    Log.Debug($"{tool} did not detach {mountPoint} within 2 s");
                    return false;
                }

                if (process.ExitCode != 0)
                {
                    Log.Debug($"{tool} did not detach {mountPoint}: {errors.Trim()}");
                }

                return process.ExitCode == 0;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                continue;
            }
        }

        Log.Debug($"neither fusermount3 nor fusermount is installed, so {mountPoint} stays mounted");
        return false;
    }

    private static string Unescape(string field)
    {
        if (!field.Contains('\\', StringComparison.Ordinal))
        {
            return field;
        }

        var text = new StringBuilder(field.Length);
        for (var i = 0; i < field.Length; i++)
        {
            if (field[i] == '\\' && IsOctal(field, i + 1))
            {
                text.Append((char)Convert.ToInt32(field.Substring(i + 1, 3), 8));
                i += 3;
                continue;
            }

            text.Append(field[i]);
        }

        return text.ToString();
    }

    private static bool IsOctal(string field, int start) =>
        start + 3 <= field.Length && field.AsSpan(start, 3).IndexOfAnyExcept("01234567") < 0;
}
