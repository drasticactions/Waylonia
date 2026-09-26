using System.Diagnostics;
using System.Runtime.Versioning;
using static Waylonia.Agent.AgentLog;

namespace Waylonia.Agent;

[SupportedOSPlatform("linux")]
internal sealed class AgentRuntimeDirectory : IDisposable
{
    private static readonly TimeSpan MountGrace = TimeSpan.FromSeconds(1);

    private bool _disposed;

    private AgentRuntimeDirectory(string path) => Path = path;

    public string Path { get; }

    public string WaylandSocket => System.IO.Path.Combine(Path, AgentEnvironment.WaylandName);

    public static AgentRuntimeDirectory? Create(string profileName, out string? error)
    {
        var parent = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (string.IsNullOrEmpty(parent) || !System.IO.Path.IsPathRooted(parent))
        {
            parent = System.IO.Path.GetTempPath();
        }

        var path = System.IO.Path.Combine(parent, $"waylonia-agent-{profileName}-{Environment.ProcessId}");
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }

            Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            error = $"the agent's runtime directory {path} cannot be created: {failure.Message}";
            return null;
        }

        error = null;
        return new AgentRuntimeDirectory(path);
    }

    public bool LinkWayland(string hostSocket)
    {
        ArgumentException.ThrowIfNullOrEmpty(hostSocket);
        var target = System.IO.Path.IsPathRooted(hostSocket)
            ? hostSocket
            : System.IO.Path.Combine(Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") ?? "/", hostSocket);
        try
        {
            File.Delete(WaylandSocket);
            File.CreateSymbolicLink(WaylandSocket, target);
            return true;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            Log.Error($"the agent's Wayland socket link {WaylandSocket} cannot be made: {failure.Message}");
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ReleaseMounts();
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"the agent's runtime directory {Path} was not removed: {failure.Message}");
        }
    }

    private void ReleaseMounts()
    {
        var deadline = Stopwatch.StartNew();
        var mounts = AgentMounts.Under(Path);
        while (mounts.Count > 0 && deadline.Elapsed < MountGrace)
        {
            Thread.Sleep(50);
            mounts = AgentMounts.Under(Path);
        }

        foreach (var mount in mounts)
        {
            Log.Debug($"{mount} is still mounted {MountGrace.TotalSeconds:0} s after the agent's bus ended; detaching it");
            _ = AgentMounts.Detach(mount);
        }
    }
}
