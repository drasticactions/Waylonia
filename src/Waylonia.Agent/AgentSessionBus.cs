using System.Diagnostics;
using System.Runtime.Versioning;
using static Waylonia.Agent.AgentLog;

namespace Waylonia.Agent;

[SupportedOSPlatform("linux")]
internal sealed class AgentSessionBus : IDisposable
{
    private readonly Process _process;
    private bool _disposed;

    private AgentSessionBus(Process process, string address)
    {
        _process = process;
        Address = address;
    }

    public string Address { get; }

    public int Pid => _process.Id;

    public static AgentSessionBus? Start(
        IReadOnlyDictionary<string, string> environment, string runtimeDirectory, string logPath, out string? error)
    {
        ArgumentNullException.ThrowIfNull(environment);
        var start = new ProcessStartInfo("dbus-daemon")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in (ReadOnlySpan<string>)[
            "--session", "--nofork", "--nopidfile", "--print-address=1", $"--address=unix:path={Path.Combine(runtimeDirectory, "bus")}"])
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment.Clear();
        foreach (var (name, value) in environment)
        {
            start.Environment[name] = value;
        }

        Process? process;
        try
        {
            process = Process.Start(start);
        }
        catch (Exception failure) when (failure is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            error = $"dbus-daemon could not start, so the agent's applications have no session bus: {failure.Message}";
            return null;
        }

        if (process is null)
        {
            error = "dbus-daemon could not start, so the agent's applications have no session bus";
            return null;
        }

        var errors = TextWriter.Null;
        try
        {
            errors = new StreamWriter(logPath, append: true) { AutoFlush = true };
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            Log.Debug($"dbus-daemon's log {logPath} cannot be opened: {failure.Message}");
        }

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is { } line)
            {
                lock (errors)
                {
                    errors.WriteLine(line);
                }
            }
        };
        process.BeginErrorReadLine();
        var line = process.StandardOutput.ReadLineAsync();
        if (!line.Wait(TimeSpan.FromSeconds(5)) || line.Result is not { Length: > 0 } address)
        {
            Kill(process);
            error = "dbus-daemon printed no address within 5 s, so the agent's applications have no session bus";
            return null;
        }

        error = null;
        Log.Debug($"the agent's session bus is {address} (pid {process.Id})");
        return new AgentSessionBus(process, address.Trim());
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Kill(_process);
        _process.Dispose();
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(2000);
            }
        }
        catch (Exception failure) when (failure is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Log.Debug($"dbus-daemon did not end cleanly: {failure.Message}");
        }
    }
}
