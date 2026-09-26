using System.Diagnostics;
using Tmds.DBus.Protocol;

namespace Waylonia.Accessibility.Tests.Live;

internal sealed class LiveEnvironment : IAsyncDisposable
{
    public const string BusLauncher = "/usr/lib/at-spi-bus-launcher";
    public const string Registry = "/usr/lib/at-spi2-registryd";

    private readonly List<Process> _processes = [];
    private readonly Dictionary<int, System.Text.StringBuilder> _output = [];
    private readonly Process _dbus;

    private LiveEnvironment(string runtimeDirectory, Process dbus, string sessionBus)
    {
        RuntimeDirectory = runtimeDirectory;
        _dbus = dbus;
        SessionBus = sessionBus;
    }

    public string RuntimeDirectory { get; }

    public string SessionBus { get; }

    public HostedCompositor Compositor { get; private set; } = null!;

    public static string? Missing(params string[] programs)
    {
        if (!OperatingSystem.IsLinux())
        {
            return "the accessibility bus exists on Linux only";
        }

        foreach (var program in new[] { "dbus-daemon" }.Concat(programs))
        {
            if (Which(program) is null)
            {
                return $"{program} is not installed, and the live accessibility tests need it";
            }
        }

        foreach (var file in new[] { BusLauncher, Registry })
        {
            if (!File.Exists(file))
            {
                return $"{file} is missing; the live accessibility tests need at-spi2-core";
            }
        }

        return null;
    }

    public static string? Which(string program)
    {
        if (Path.IsPathRooted(program))
        {
            return File.Exists(program) ? program : null;
        }

        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "/usr/bin").Split(':'))
        {
            var candidate = Path.Combine(directory, program);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public static async Task<LiveEnvironment> StartAsync(CancellationToken cancel)
    {
        var runtime = Directory.CreateTempSubdirectory("waylonia-a11y-");
        File.SetUnixFileMode(runtime.FullName, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var start = new ProcessStartInfo("dbus-daemon")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("--session");
        start.ArgumentList.Add("--nofork");
        start.ArgumentList.Add("--print-address=1");
        Prepare(start.Environment, runtime.FullName, null, null);
        var dbus = Process.Start(start) ?? throw new InvalidOperationException("dbus-daemon did not start");
        dbus.ErrorDataReceived += (_, _) => { };
        dbus.BeginErrorReadLine();
        var address = await dbus.StandardOutput.ReadLineAsync(cancel).AsTask()
            .WaitAsync(TimeSpan.FromSeconds(10), cancel).ConfigureAwait(false);
        if (string.IsNullOrEmpty(address))
        {
            dbus.Kill();
            throw new InvalidOperationException("dbus-daemon printed no address");
        }

        var environment = new LiveEnvironment(runtime.FullName, dbus, address);
        environment.Compositor = new HostedCompositor();
        return environment;
    }

    public async Task StartRegistryAsync(CancellationToken cancel)
    {
        Launch(Registry);
        using var session = new DBusConnection(SessionBus);
        await session.ConnectAsync();
        var address = await new BusAddressReader(session).GetAsync().WaitAsync(cancel).ConfigureAwait(false);
        using var a11y = new DBusConnection(address);
        await a11y.ConnectAsync();
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (!(await a11y.ListServicesAsync()).Contains("org.a11y.atspi.Registry"))
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("at-spi2-registryd never took its name on the accessibility bus");
            }

            await Task.Delay(50, cancel).ConfigureAwait(false);
        }
    }

    public Process Launch(string program, params string[] arguments)
    {
        var start = new ProcessStartInfo(program)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        Prepare(start.Environment, RuntimeDirectory, SessionBus, Compositor?.SocketPath);
        var process = Process.Start(start) ?? throw new InvalidOperationException($"{program} did not start");
        var output = new System.Text.StringBuilder();
        lock (_output)
        {
            _output[process.Id] = output;
        }

        process.OutputDataReceived += (_, line) =>
        {
            if (line.Data is { } data)
            {
                lock (output)
                {
                    output.AppendLine(data);
                }
            }
        };
        process.ErrorDataReceived += (_, _) => { };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        lock (_processes)
        {
            _processes.Add(process);
        }

        return process;
    }

    public string Output(Process process)
    {
        System.Text.StringBuilder? output;
        lock (_output)
        {
            _output.TryGetValue(process.Id, out output);
        }

        if (output is null)
        {
            return string.Empty;
        }

        lock (output)
        {
            return output.ToString();
        }
    }

    private static void Prepare(IDictionary<string, string?> environment, string runtime, string? bus, string? wayland)
    {
        environment["XDG_RUNTIME_DIR"] = runtime;
        environment.Remove("NO_AT_BRIDGE");
        environment.Remove("DISPLAY");
        environment.Remove("AT_SPI_BUS_ADDRESS");
        environment["GTK_A11Y"] = "atspi";
        environment["GDK_BACKEND"] = "wayland";
        environment["GSK_RENDERER"] = "cairo";
        environment["GDK_DEBUG"] = "no-portals";
        environment["GTK_USE_PORTAL"] = "0";
        environment["QT_QPA_PLATFORM"] = "wayland";
        environment["QT_LINUX_ACCESSIBILITY_ALWAYS_ON"] = "1";
        environment["QT_ACCESSIBILITY"] = "1";
        environment["MOZ_ENABLE_WAYLAND"] = "1";
        environment["GNOME_ACCESSIBILITY"] = "1";
        if (bus is null)
        {
            environment.Remove("DBUS_SESSION_BUS_ADDRESS");
        }
        else
        {
            environment["DBUS_SESSION_BUS_ADDRESS"] = bus;
        }

        if (wayland is null)
        {
            environment.Remove("WAYLAND_DISPLAY");
        }
        else
        {
            environment["WAYLAND_DISPLAY"] = wayland;
        }
    }

    public async ValueTask DisposeAsync()
    {
        List<Process> processes;
        lock (_processes)
        {
            processes = [.. _processes];
        }

        foreach (var process in processes.AsEnumerable().Reverse())
        {
            Stop(process);
        }

        Stop(_dbus);
        foreach (var stray in Strays())
        {
            try
            {
                using var process = Process.GetProcessById(stray);
                process.Kill();
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
            }
        }

        Compositor?.Dispose();
        await Task.Yield();
        try
        {
            Directory.Delete(RuntimeDirectory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private IEnumerable<int> Strays()
    {
        var marker = System.Text.Encoding.UTF8.GetBytes("XDG_RUNTIME_DIR=" + RuntimeDirectory + "\0");
        var self = Environment.ProcessId;
        foreach (var directory in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(directory), out var pid) || pid == self)
            {
                continue;
            }

            byte[] environ;
            try
            {
                environ = File.ReadAllBytes(Path.Combine(directory, "environ"));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            if (environ.AsSpan().IndexOf(marker) >= 0)
            {
                yield return pid;
            }
        }
    }

    private static void Stop(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
        catch (InvalidOperationException)
        {
        }

        process.Dispose();
    }
}
