using Basin.Ipc;

namespace Waylonia.Agent;

internal sealed class AgentInterceptor : IIpcInterceptor
{
    private readonly AgentProfile _profile;
    private readonly AgentTakeover _takeover;
    private readonly IpcApprovalBroker _broker;
    private readonly AgentAudit? _audit;
    private readonly Func<ReadOnlySpan<byte>, AgentLaunchPlan?> _planLaunch;
    private readonly Dictionary<(long Client, string Method), Queue<Pending>> _pending = [];
    private readonly Dictionary<string, int> _launchTokens = new(StringComparer.Ordinal);
    private readonly HashSet<string> _launchesAllowedForRun = new(StringComparer.Ordinal);

    public AgentInterceptor(
        AgentProfile profile,
        AgentTakeover takeover,
        IpcApprovalBroker broker,
        AgentAudit? audit,
        Func<ReadOnlySpan<byte>, AgentLaunchPlan?> planLaunch)
    {
        _profile = profile;
        _takeover = takeover;
        _broker = broker;
        _audit = audit;
        _planLaunch = planLaunch;
    }

    public IpcServer? Server { get; set; }

    public AgentShots? Shots { get; set; }

    public IpcClientState? Internal { get; set; }

    public IReadOnlyCollection<string> LaunchesAllowedForRun => _launchesAllowedForRun;

    public bool ConsumeLaunchApproval(string key)
    {
        if (!_launchTokens.TryGetValue(key, out var count) || count <= 0)
        {
            return false;
        }

        Take(key, count);
        return true;
    }

    public IpcDecision Before(string method, ReadOnlySpan<byte> parameters, IpcCallContext context)
    {
        if (ReferenceEquals(context.Client, Internal))
        {
            return IpcDecision.Allow;
        }

        var pending = new Pending(method, parameters.ToArray(), DateTimeOffset.UtcNow);
        var key = (context.Client.Id, method);
        if (!_pending.TryGetValue(key, out var queue))
        {
            _pending[key] = queue = new Queue<Pending>();
        }

        queue.Enqueue(pending);
        if (_takeover.IsPaused && AgentTakeover.Refuses(method))
        {
            return IpcDecision.Deny(AgentTakeover.PausedMessage);
        }

        var decision = Gate(method, parameters, context, pending);
        if (decision.Kind != IpcDecisionKind.Deny && ShotsFor(method))
        {
            pending.Before = Shots?.Take("before");
        }

        return decision;
    }

    public void After(string method, IpcCallOutcome outcome, TimeSpan elapsed, IpcCallContext context)
    {
        if (ReferenceEquals(context.Client, Internal))
        {
            return;
        }

        var key = (context.Client.Id, method);
        if (!_pending.TryGetValue(key, out var queue) || !queue.TryDequeue(out var pending))
        {
            pending = new Pending(method, [], DateTimeOffset.UtcNow);
        }
        else if (queue.Count == 0)
        {
            _pending.Remove(key);
        }

        var error = outcome.Succeeded ? null : $"{outcome.ErrorCode}: {outcome.ErrorMessage}";
        if (outcome.Succeeded && ShotsFor(method) && Shots is { } shots)
        {
            shots.TakeWhenQuiet(after => Write(pending, error, elapsed, after));
            return;
        }

        Write(pending, error, elapsed, null);
    }

    public void Note(string name, params (string Key, string? Value)[] fields) =>
        _audit?.Line(AgentAuditFormat.Event(DateTimeOffset.UtcNow, name, fields));

    private IpcDecision Gate(string method, ReadOnlySpan<byte> parameters, IpcCallContext context, Pending pending)
    {
        if (AgentApprovals.ActionOf(method) is not { } action || !_profile.Gates(action))
        {
            return IpcDecision.Allow;
        }

        string reason;
        string? launchKey = null;
        if (method == "waylonia/launch")
        {
            if (_planLaunch(parameters) is not { Listed: false } plan)
            {
                return IpcDecision.Allow;
            }

            launchKey = AgentLauncher.Key(plan);
            if (_launchesAllowedForRun.Contains(launchKey))
            {
                Give(launchKey);
                pending.Approval = "allowed_for_run";
                return IpcDecision.Allow;
            }

            reason = $"{AgentApprovals.Reason(action)}: {string.Join(' ', plan.Argv)}";
        }
        else
        {
            if (_broker.IsAllowedForRun(method))
            {
                pending.Approval = "allowed_for_run";
                return IpcDecision.Allow;
            }

            reason = AgentApprovals.Reason(action);
        }

        if (launchKey is not null)
        {
            Give(launchKey);
        }

        var approval = _broker.Request(context.Hold(), reason);
        pending.Request = approval;
        AgentLog.Log.Info($"approval {approval.Id} for {method}: {reason}");
        Note("approval-requested", ("id", Id(approval.Id)), ("method", method), ("reason", reason));
        if (approval.IsAnswered)
        {
            Answered(approval, launchKey, pending);
            return IpcDecision.Defer;
        }

        _takeover.PendingApprovals++;
        approval.Answered += answered =>
        {
            _takeover.PendingApprovals = Math.Max(0, _takeover.PendingApprovals - 1);
            Answered(answered, launchKey, pending);
        };
        return IpcDecision.Defer;
    }

    private void Answered(IpcApproval approval, string? launchKey, Pending pending)
    {
        var answer = Name(approval.Answer);
        pending.Approval = answer;
        Note("approval-answered", ("id", Id(approval.Id)), ("method", approval.Method), ("answer", answer));
        if (launchKey is null)
        {
            return;
        }

        if (approval.Answer == IpcApprovalAnswer.AllowRun)
        {
            _launchesAllowedForRun.Add(launchKey);
        }

        if (approval.Answer is not (IpcApprovalAnswer.AllowOnce or IpcApprovalAnswer.AllowRun)
            && _launchTokens.TryGetValue(launchKey, out var count))
        {
            Take(launchKey, count);
        }
    }

    private void Give(string key) => _launchTokens[key] = _launchTokens.GetValueOrDefault(key) + 1;

    private void Take(string key, int count)
    {
        if (count <= 1)
        {
            _launchTokens.Remove(key);
        }
        else
        {
            _launchTokens[key] = count - 1;
        }
    }

    private bool ShotsFor(string method)
    {
        if (_audit is null || Shots is null || _profile.Screenshots == AgentScreenshots.None)
        {
            return false;
        }

        if (_profile.Screenshots == AgentScreenshots.All)
        {
            return true;
        }

        return Server is { } server && server.Methods.TryGetInfo(method, out var info)
            ? (info.Traits & IpcMethodTraits.ReadOnly) == 0
            : !method.StartsWith("ipc/", StringComparison.Ordinal);
    }

    private void Write(Pending pending, string? error, TimeSpan elapsed, string? after) =>
        _audit?.Line(AgentAuditFormat.Call(
            pending.Time, pending.Method, pending.Parameters, _profile.AuditText, error, elapsed.TotalMilliseconds,
            pending.Before, after, pending.Approval ?? (pending.Request?.Answer is { } answer ? Name(answer) : null)));

    private static string Id(long id) => id.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public static string Name(IpcApprovalAnswer? answer) => answer switch
    {
        IpcApprovalAnswer.AllowOnce => "allow_once",
        IpcApprovalAnswer.AllowRun => "allow_run",
        IpcApprovalAnswer.Deny => "deny",
        IpcApprovalAnswer.TimedOut => "timed_out",
        IpcApprovalAnswer.NoAnswerer => "no_answerer",
        _ => "none",
    };

    private sealed class Pending(string method, byte[] parameters, DateTimeOffset time)
    {
        public string Method { get; } = method;

        public byte[] Parameters { get; } = parameters;

        public DateTimeOffset Time { get; } = time;

        public string? Before { get; set; }

        public string? Approval { get; set; }

        public IpcApproval? Request { get; set; }
    }
}
