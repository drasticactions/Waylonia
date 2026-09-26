namespace Waylonia.Agent;

internal sealed class AgentTakeover
{
    public const string PausedMessage = "paused: someone is using the window";

    private long _lastHostInput;
    public AgentTakeover(int timeoutSeconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(timeoutSeconds);
        TimeoutMs = timeoutSeconds * 1000L;
    }

    public long TimeoutMs { get; }

    public AgentTakeoverState State { get; private set; }

    public bool IsPaused => State == AgentTakeoverState.Paused;

    private int _pendingApprovals;

    public int PendingApprovals
    {
        get => _pendingApprovals;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            if (_pendingApprovals == value)
            {
                return;
            }

            _pendingApprovals = value;
            Changed?.Invoke();
        }
    }

    public event Action? Changed;

    public string Status => (PendingApprovals > 0, IsPaused) switch
    {
        (true, true) => "Agent: paused, waiting for approval",
        (true, false) => "Agent: waiting for approval",
        (false, true) => "Agent: paused",
        _ => "Agent: driving",
    };

    public bool HostInput(long nowMs, bool pressOrKey)
    {
        if (!pressOrKey)
        {
            return false;
        }

        _lastHostInput = nowMs;
        if (IsPaused)
        {
            return false;
        }

        State = AgentTakeoverState.Paused;
        Changed?.Invoke();
        return true;
    }

    public bool Resume()
    {
        if (!IsPaused)
        {
            return false;
        }

        State = AgentTakeoverState.Driving;
        Changed?.Invoke();
        return true;
    }

    public bool Tick(long nowMs) => IsPaused && TimeoutMs > 0 && nowMs - _lastHostInput >= TimeoutMs && Resume();

    public long? ResumesAt => IsPaused && TimeoutMs > 0 ? _lastHostInput + TimeoutMs : null;

    public static bool Refuses(string method) => method.StartsWith("input/", StringComparison.Ordinal) || method == "waylonia/a11y-click";
}
