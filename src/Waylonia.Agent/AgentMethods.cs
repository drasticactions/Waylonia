using System.Globalization;
using System.Runtime.Versioning;
using System.Text.Json;
using Basin.Ipc;
using Basin.Shell.Nested;

namespace Waylonia.Agent;

[SupportedOSPlatform("linux")]
internal static class AgentMethods
{
    public static void Register(IpcServer server, AgentRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(runtime);
        server.Methods.Register("waylonia/describe", (ref IpcParams _, IpcReply reply) => Describe(runtime, server, reply), info: AgentSchemas.Describe);
        server.Methods.Register("waylonia/launchable", (ref IpcParams _, IpcReply reply) => Launchable(runtime, reply), info: AgentSchemas.Launchable);
        server.Methods.Register("waylonia/launch", (ref IpcParams parameters, IpcReply reply) => Launch(runtime, server, ref parameters, reply), info: AgentSchemas.Launch);
        server.Methods.Register("waylonia/window-info", (ref IpcParams parameters, IpcReply reply) => WindowInfo(runtime, server, ref parameters, reply), info: AgentSchemas.WindowInfo);
    }

    public const string Boundary =
        "the control socket is the boundary and the uid is the socket's boundary: approvals and the audit log bind the tool path, and they do not contain a process that runs as this user";

    private static void Describe(AgentRuntime runtime, IpcServer server, IpcReply reply)
    {
        var profile = runtime.Profile;
        var w = reply.Result;
        w.WriteStartObject();
        w.WriteString("profile", profile.Name);
        w.WriteString("directory", profile.Directory);
        w.WriteString("mode", runtime.Headless ? "headless" : "visible");
        w.WriteStartObject("output");
        w.WriteNumber("width", profile.Width);
        w.WriteNumber("height", profile.Height);
        w.WriteNumber("scale", profile.Scale);
        w.WriteEndObject();
        w.WriteString("wayland_display", runtime.WaylandSocket);
        WriteNullable(w, "xwayland_display", runtime.XDisplay?.Invoke());
        WriteNullable(w, "session_bus", runtime.BusAddress);
        w.WriteStartArray("allowlist");
        foreach (var entry in profile.Allow.Entries)
        {
            w.WriteStringValue(entry.ToString());
        }

        w.WriteEndArray();
        w.WriteBoolean("ask", profile.Ask);
        w.WriteString("launch", profile.LaunchSummary);
        w.WriteStartArray("approve");
        foreach (var action in profile.Approve)
        {
            w.WriteStringValue(action);
        }

        w.WriteEndArray();
        w.WriteStartArray("omitted");
        foreach (var name in AgentRuntime.Omitted)
        {
            w.WriteStringValue(name);
        }

        w.WriteEndArray();
        w.WriteString("accessibility", runtime.AccessibilityStatus);
        w.WriteString("takeover", runtime.Takeover.Status);
        w.WriteBoolean("virtual_input", false);
        w.WriteBoolean("clipboard_isolated", true);
        w.WriteString("processes", "every process that waylonia/launch starts is ended when this run ends");
        w.WriteString("audit", runtime.Audit.Path);
        w.WriteString("boundary", Boundary);
        w.WriteNumber("running", server.Processes.Processes.Count(static process => process.IsRunning));
        w.WriteEndObject();
    }

    private static void Launchable(AgentRuntime runtime, IpcReply reply)
    {
        var w = reply.Result;
        w.WriteStartObject();
        w.WriteString("launch", runtime.Profile.LaunchSummary);
        w.WriteStartArray("entries");
        foreach (var entry in runtime.Profile.Allow.Entries)
        {
            w.WriteStartObject();
            w.WriteString("entry", entry.ToString());
            if (entry.IsDesktop)
            {
                w.WriteString("desktop_id", entry.DesktopId);
                var desktop = runtime.FindDesktop(entry.DesktopId);
                WriteNullable(w, "name", desktop?.Name);
                WriteNullable(w, "comment", desktop?.Comment);
                w.WriteStartArray("categories");
                foreach (var category in desktop?.Categories ?? [])
                {
                    w.WriteStringValue(category);
                }

                w.WriteEndArray();
                w.WriteBoolean("installed", desktop is not null);
            }
            else
            {
                w.WriteString("command", entry.Program);
                w.WriteStartArray("args");
                foreach (var argument in entry.Arguments)
                {
                    w.WriteStringValue(argument);
                }

                w.WriteEndArray();
            }

            w.WriteEndObject();
        }

        w.WriteEndArray();
        w.WriteEndObject();
    }

    private static void Launch(AgentRuntime runtime, IpcServer server, ref IpcParams parameters, IpcReply reply)
    {
        string? command = parameters.TryGetString("command", out var c) ? c : null;
        string? desktopId = parameters.TryGetString("desktop_id", out var d) ? d : null;
        var args = parameters.Has("args") && parameters.TryGetStringArray("args", out var list) ? list : [];
        if (parameters.Failed)
        {
            return;
        }

        var profile = runtime.Profile;
        if (AgentLauncher.Plan(profile.Allow, command, args, desktopId, runtime.FindDesktop, out var error) is not { } plan)
        {
            reply.Error(IpcErrorCodes.InvalidParams, error!);
            return;
        }

        if (!plan.Listed && !runtime.Interceptor.ConsumeLaunchApproval(AgentLauncher.Key(plan)))
        {
            reply.Error(IpcErrorCodes.Refused, profile.Gates(AgentApprovals.LaunchUnlisted)
                ? $"{AgentLauncher.Key(plan)} is not in the allowlist and no one approved it"
                : $"{AgentLauncher.Key(plan)} is not in the allowlist of agent profile {profile.Name}; add it to [launch] allow in {profile.File}, or set ask = true there to ask a person");
            return;
        }

        var environment = runtime.LaunchEnvironment();
        var log = runtime.NextLogPath(plan.Name);
        var launched = server.Processes.Spawn(
            new IpcLaunch(plan.Argv) { Env = environment.Env, UnsetEnv = environment.Unset, Cwd = profile.Home, LogPath = log },
            out var spawnError);
        if (launched is null)
        {
            reply.Error(IpcErrorCodes.Failed, $"{plan.Argv[0]} did not start: {spawnError}");
            return;
        }

        var w = reply.Result;
        w.WriteStartObject();
        w.WriteNumber("launch_id", launched.LaunchId);
        w.WriteNumber("pid", launched.Pid);
        w.WriteStartArray("argv");
        foreach (var part in plan.Argv)
        {
            w.WriteStringValue(part);
        }

        w.WriteEndArray();
        w.WriteString("log", log);
        w.WriteBoolean("listed", plan.Listed);
        w.WriteEndObject();
    }

    private static void WindowInfo(AgentRuntime runtime, IpcServer server, ref IpcParams parameters, IpcReply reply)
    {
        ulong? only = parameters.Has("id") ? parameters.GetUlong("id") : null;
        if (parameters.Failed || runtime.Shell is not { } shell)
        {
            return;
        }

        var rows = new List<AgentWindowRow>();
        foreach (var window in shell.Windows)
        {
            if (!window.IsMapped || shell.ToplevelIdOf(window) is var id && id == 0 || only is { } wanted && wanted != id)
            {
                continue;
            }

            rows.Add(Row(runtime, server, shell, window, id));
        }

        if (only is { } missing && rows.Count == 0)
        {
            reply.Error(IpcErrorCodes.NotFound, $"no window {missing.ToString(CultureInfo.InvariantCulture)}");
            return;
        }

        if (runtime.Accessibility is not { } a11y || runtime.Compositor is not { } compositor)
        {
            WriteRows(reply, rows, runtime.AccessibilityStatus);
            return;
        }

        var pending = reply.Defer();
        _ = Task.Run(async () =>
        {
            var found = new bool[rows.Count];
            string? problem = null;
            for (var i = 0; i < rows.Count; i++)
            {
                try
                {
                    found[i] = await a11y.FindAsync(rows[i].Target, CancellationToken.None).ConfigureAwait(false) is not null;
                }
                catch (Exception failure) when (failure is not OutOfMemoryException)
                {
                    problem = failure.Message;
                }
            }

            compositor.Post(() =>
            {
                for (var i = 0; i < rows.Count; i++)
                {
                    rows[i] = rows[i] with { Accessible = found[i] };
                }

                WriteRows(pending, rows, problem);
                _ = pending.Complete();
            });
        });
    }

    private static AgentWindowRow Row(AgentRuntime runtime, IpcServer server, NestedShell shell, ManagedWindow window, ulong id)
    {
        var pid = shell.Toplevels is { } model && model.TryGet(id, out var info) ? (int)info.Pid : 0;
        long? launch = null;
        if (pid > 0)
        {
            foreach (var process in server.Processes.Processes)
            {
                if (process.IsRunning && ProcessTree.IsDescendant(pid, process.Pid))
                {
                    launch = process.LaunchId;
                    break;
                }
            }
        }

        ulong parent = 0;
        if (window.Content.Parent is { } parentContent)
        {
            foreach (var other in shell.Windows)
            {
                if (ReferenceEquals(other.Content, parentContent))
                {
                    parent = shell.ToplevelIdOf(other);
                    break;
                }
            }
        }

        var x11 = window.Content is not XdgContent && window.Content.UISurface is null;
        return new AgentWindowRow(id, pid, launch, x11, window.IsDialog, parent, AgentAccessibility.TargetOf(window, pid), false);
    }

    private static void WriteRows(IpcReply reply, List<AgentWindowRow> rows, string? accessibilityNote)
    {
        var w = reply.Result;
        w.WriteStartObject();
        w.WriteStartArray("windows");
        foreach (var row in rows)
        {
            w.WriteStartObject();
            w.WriteNumber("id", row.Id);
            w.WriteNumber("pid", row.Pid);
            if (row.Launch is { } launch)
            {
                w.WriteNumber("launched", launch);
            }
            else
            {
                w.WriteNull("launched");
            }

            w.WriteBoolean("x11", row.X11);
            w.WriteBoolean("dialog", row.Dialog);
            if (row.Parent != 0)
            {
                w.WriteNumber("parent", row.Parent);
            }
            else
            {
                w.WriteNull("parent");
            }

            w.WriteBoolean("accessible", row.Accessible);
            w.WriteEndObject();
        }

        w.WriteEndArray();
        WriteNullable(w, "accessibility", accessibilityNote);
        w.WriteEndObject();
    }

    private static void WriteNullable(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is null)
        {
            writer.WriteNull(name);
        }
        else
        {
            writer.WriteString(name, value);
        }
    }
}
