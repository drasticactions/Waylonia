using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using Basin.Ipc;
using Waylonia.Accessibility;

namespace Waylonia.Agent;

[SupportedOSPlatform("linux")]
internal static class AgentA11yMethods
{
    public static void Register(IpcServer server, AgentRuntime runtime, AgentAccessibility a11y)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(a11y);
        server.Methods.Register("waylonia/a11y-tree", (ref IpcParams p, IpcReply r) => Tree(runtime, a11y, ref p, r), info: AgentSchemas.A11yTree);
        server.Methods.Register("waylonia/a11y-find", (ref IpcParams p, IpcReply r) => Find(runtime, a11y, ref p, r), info: AgentSchemas.A11yFind);
        server.Methods.Register("waylonia/a11y-action", (ref IpcParams p, IpcReply r) => Action(runtime, a11y, ref p, r), info: AgentSchemas.A11yAction);
        server.Methods.Register("waylonia/a11y-set-text", (ref IpcParams p, IpcReply r) => SetText(runtime, a11y, ref p, r), info: AgentSchemas.A11ySetText);
        server.Methods.Register("waylonia/a11y-set-value", (ref IpcParams p, IpcReply r) => SetValue(runtime, a11y, ref p, r), info: AgentSchemas.A11ySetValue);
        server.Methods.Register("waylonia/a11y-focus", (ref IpcParams p, IpcReply r) => Focus(runtime, a11y, ref p, r), info: AgentSchemas.A11yFocus);
        server.Methods.Register("waylonia/a11y-click", (ref IpcParams p, IpcReply r) => Click(runtime, server, a11y, ref p, r), info: AgentSchemas.A11yClick);
        server.Methods.Register("waylonia/a11y-wait-text", (ref IpcParams p, IpcReply r) => WaitText(runtime, a11y, ref p, r), info: AgentSchemas.A11yWaitText);
    }

    private static void Tree(AgentRuntime runtime, AgentAccessibility a11y, ref IpcParams parameters, IpcReply reply)
    {
        var id = parameters.GetUlong("window");
        var depth = parameters.TryGetInt("depth", out var d) ? (int)Math.Clamp(d, 1, 64) : 12;
        var max = parameters.TryGetInt("max_nodes", out var m) ? (int)Math.Clamp(m, 1, 5000) : 400;
        if (parameters.Failed || Target(runtime, id, reply) is not { } target)
        {
            return;
        }

        Run(runtime, reply, async cancel =>
        {
            var service = await a11y.ServiceAsync(cancel).ConfigureAwait(false);
            var window = await Window(service, target, id, cancel).ConfigureAwait(false);
            var text = await service.TreeTextAsync(window, depth, max, cancel).ConfigureAwait(false);
            return w =>
            {
                w.WriteStartObject();
                w.WriteNumber("window", id);
                w.WriteString("application", window.Application);
                w.WriteString("root", window.RootId);
                w.WriteString("tree", text);
                w.WriteEndObject();
            };
        });
    }

    private static void Find(AgentRuntime runtime, AgentAccessibility a11y, ref IpcParams parameters, IpcReply reply)
    {
        var id = parameters.GetUlong("window");
        string? role = parameters.TryGetString("role", out var r) ? r : null;
        string? name = parameters.TryGetString("name", out var n) ? n : null;
        IReadOnlyList<string> states = parameters.Has("states") && parameters.TryGetStringArray("states", out var s) ? s : [];
        var max = parameters.TryGetInt("max", out var m) ? (int)Math.Clamp(m, 1, 500) : 50;
        if (parameters.Failed || Target(runtime, id, reply) is not { } target)
        {
            return;
        }

        Run(runtime, reply, async cancel =>
        {
            var service = await a11y.ServiceAsync(cancel).ConfigureAwait(false);
            var window = await Window(service, target, id, cancel).ConfigureAwait(false);
            var nodes = await service.FindAsync(window, role, name, states, max, cancel).ConfigureAwait(false);
            return w =>
            {
                w.WriteStartObject();
                w.WriteNumber("window", id);
                w.WriteStartArray("nodes");
                foreach (var node in nodes)
                {
                    WriteNode(w, node);
                }

                w.WriteEndArray();
                w.WriteEndObject();
            };
        });
    }

    private static void Action(AgentRuntime runtime, AgentAccessibility a11y, ref IpcParams parameters, IpcReply reply)
    {
        var node = parameters.GetString("node");
        string? action = parameters.TryGetString("action", out var a) ? a : null;
        if (parameters.Failed)
        {
            return;
        }

        Run(runtime, reply, async cancel =>
        {
            var service = await a11y.ServiceAsync(cancel).ConfigureAwait(false);
            await service.DoActionAsync(node, action, cancel).ConfigureAwait(false);
            return w => Done(w, node);
        });
    }

    private static void SetText(AgentRuntime runtime, AgentAccessibility a11y, ref IpcParams parameters, IpcReply reply)
    {
        var node = parameters.GetString("node");
        var text = parameters.GetString("text");
        if (parameters.Failed)
        {
            return;
        }

        Run(runtime, reply, async cancel =>
        {
            var service = await a11y.ServiceAsync(cancel).ConfigureAwait(false);
            await service.SetTextAsync(node, text, cancel).ConfigureAwait(false);
            return w => Done(w, node);
        });
    }

    private static void SetValue(AgentRuntime runtime, AgentAccessibility a11y, ref IpcParams parameters, IpcReply reply)
    {
        var node = parameters.GetString("node");
        var value = parameters.GetDouble("value");
        if (parameters.Failed)
        {
            return;
        }

        Run(runtime, reply, async cancel =>
        {
            var service = await a11y.ServiceAsync(cancel).ConfigureAwait(false);
            await service.SetValueAsync(node, value, cancel).ConfigureAwait(false);
            return w => Done(w, node);
        });
    }

    private static void Focus(AgentRuntime runtime, AgentAccessibility a11y, ref IpcParams parameters, IpcReply reply)
    {
        var node = parameters.GetString("node");
        if (parameters.Failed)
        {
            return;
        }

        Run(runtime, reply, async cancel =>
        {
            var service = await a11y.ServiceAsync(cancel).ConfigureAwait(false);
            await service.GrabFocusAsync(node, cancel).ConfigureAwait(false);
            return w => Done(w, node);
        });
    }

    private static void WaitText(AgentRuntime runtime, AgentAccessibility a11y, ref IpcParams parameters, IpcReply reply)
    {
        var id = parameters.GetUlong("window");
        var text = parameters.GetString("text");
        var timeout = parameters.TryGetInt("timeout_ms", out var t) ? (int)Math.Clamp(t, 1, 120000) : 5000;
        if (parameters.Failed || Target(runtime, id, reply) is not { } target)
        {
            return;
        }

        Run(runtime, reply, async cancel =>
        {
            var service = await a11y.ServiceAsync(cancel).ConfigureAwait(false);
            var window = await Window(service, target, id, cancel).ConfigureAwait(false);
            var node = await service.WaitTextAsync(window, text, TimeSpan.FromMilliseconds(timeout), cancel).ConfigureAwait(false)
                ?? throw new AgentCallException(
                    IpcErrorCodes.Failed,
                    $"no node under window {Id(id)} had a name or text containing '{text}' within {Id((ulong)timeout)} ms");
            return w =>
            {
                w.WriteStartObject();
                w.WritePropertyName("node");
                WriteNode(w, node);
                w.WriteEndObject();
            };
        });
    }

    private static void Click(AgentRuntime runtime, IpcServer server, AgentAccessibility a11y, ref IpcParams parameters, IpcReply reply)
    {
        var id = parameters.GetUlong("window");
        var node = parameters.GetString("node");
        var button = parameters.TryGetString("button", out var b) ? b : "left";
        if (parameters.Failed || Target(runtime, id, reply) is not { } target)
        {
            return;
        }

        if (button is not ("left" or "right" or "middle"))
        {
            reply.Error(IpcErrorCodes.InvalidParams, "'button' is left, right or middle");
            return;
        }

        var pending = reply.Defer();
        _ = Task.Run(async () =>
        {
            A11yBox box;
            try
            {
                var service = await a11y.ServiceAsync(CancellationToken.None).ConfigureAwait(false);
                var window = await Window(service, target, id, CancellationToken.None).ConfigureAwait(false);
                box = await service.ExtentsAsync(window, node, CancellationToken.None).ConfigureAwait(false)
                    ?? throw new AgentCallException(IpcErrorCodes.Failed, $"node {node} reports no box, so there is nothing to click; try waylonia/a11y-action");
            }
            catch (Exception failure) when (failure is A11yException or AgentCallException)
            {
                runtime.Compositor!.Post(() => Fail(pending, failure));
                return;
            }

            var x = box.X + (box.Width / 2);
            var y = box.Y + (box.Height / 2);
            runtime.Compositor!.Post(() =>
            {
                var sink = new AgentInternalSink(runtime.Interceptor.Internal!);
                var inner = new IpcReply(sink);
                var json = Encoding.UTF8.GetBytes(
                    $$"""{"window":{{Id(id)}},"x":{{x.ToString(CultureInfo.InvariantCulture)}},"y":{{y.ToString(CultureInfo.InvariantCulture)}},"button":"{{button}}"}""");
                server.Invoke("input/pointer-button", default, json, inner);
                if (inner.ErrorCode is { } code)
                {
                    pending.Error(code, $"the click at ({x}, {y}) in window {Id(id)} was not sent: {inner.ErrorMessage}");
                }
                else
                {
                    var w = pending.Result;
                    w.WriteStartObject();
                    w.WriteString("node", node);
                    w.WriteNumber("x", x);
                    w.WriteNumber("y", y);
                    w.WriteEndObject();
                }

                _ = pending.Complete();
            });
        });
    }

    private static A11yWindowTarget? Target(AgentRuntime runtime, ulong id, IpcReply reply)
    {
        if (runtime.Shell is not { } shell)
        {
            reply.Error(IpcErrorCodes.Unavailable, "the shell is not up");
            return null;
        }

        foreach (var window in shell.Windows)
        {
            if (window.IsMapped && shell.ToplevelIdOf(window) == id)
            {
                var pid = shell.Toplevels is { } model && model.TryGet(id, out var info) ? (int)info.Pid : 0;
                return AgentAccessibility.TargetOf(window, pid);
            }
        }

        reply.Error(IpcErrorCodes.NotFound, $"no window {Id(id)}");
        return null;
    }

    private static async Task<A11yWindow> Window(A11yService service, A11yWindowTarget target, ulong id, CancellationToken cancel) =>
        await service.FindWindowAsync(target, cancel).ConfigureAwait(false)
            ?? throw new AgentCallException(
                IpcErrorCodes.NotFound,
                $"window {Id(id)} (pid {target.Pid}, \"{target.Title}\") has no accessibility tree; the toolkit may not expose one, or it has not registered yet");

    private static void Run(AgentRuntime runtime, IpcReply reply, Func<CancellationToken, Task<Action<Utf8JsonWriter>>> work)
    {
        var pending = reply.Defer();
        var compositor = runtime.Compositor!;
        _ = Task.Run(async () =>
        {
            Action<Utf8JsonWriter>? write = null;
            Exception? failure = null;
            try
            {
                using var cancel = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                write = await work(cancel.Token).ConfigureAwait(false);
            }
            catch (Exception caught) when (caught is not OutOfMemoryException)
            {
                failure = caught;
            }

            compositor.Post(() =>
            {
                if (failure is not null)
                {
                    Fail(pending, failure);
                    return;
                }

                write!(pending.Result);
                _ = pending.Complete();
            });
        });
    }

    private static void Fail(IpcPendingReply pending, Exception failure)
    {
        var (code, message) = failure switch
        {
            AgentCallException call => (call.Code, call.Message),
            A11yException a11y => (IpcErrorCodes.Failed, a11y.Message),
            OperationCanceledException => (IpcErrorCodes.Failed, "the accessibility call took longer than 2 minutes"),
            _ => (IpcErrorCodes.Failed, $"the accessibility call failed: {failure.Message}"),
        };
        pending.Error(code, message);
        _ = pending.Complete();
    }

    private static void Done(Utf8JsonWriter w, string node)
    {
        w.WriteStartObject();
        w.WriteString("node", node);
        w.WriteEndObject();
    }

    private static void WriteNode(Utf8JsonWriter w, A11yNodeInfo node)
    {
        w.WriteStartObject();
        w.WriteString("node", node.Id);
        w.WriteString("role", node.Role);
        w.WriteString("name", node.Name);
        w.WriteStartArray("states");
        foreach (var state in node.States)
        {
            w.WriteStringValue(state);
        }

        w.WriteEndArray();
        if (node.Box is { } box)
        {
            w.WriteStartObject("box");
            w.WriteNumber("x", box.X);
            w.WriteNumber("y", box.Y);
            w.WriteNumber("width", box.Width);
            w.WriteNumber("height", box.Height);
            w.WriteEndObject();
        }

        if (node.Text is { } text)
        {
            w.WriteString("text", text);
        }

        w.WriteEndObject();
    }

    private static string Id(ulong id) => id.ToString(CultureInfo.InvariantCulture);
}
