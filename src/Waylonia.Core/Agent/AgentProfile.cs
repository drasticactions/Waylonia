using Basin.Shell.Nested;

namespace Waylonia.Agent;

internal sealed record AgentProfile(
    string Name,
    string Directory,
    int Width = AgentProfile.DefaultWidth,
    int Height = AgentProfile.DefaultHeight,
    double Scale = 1.0,
    bool Headless = false,
    AgentAllowlist? Allow = null,
    bool Ask = true,
    IReadOnlyList<string>? Approve = null,
    AgentScreenshots Screenshots = AgentScreenshots.Acting,
    bool AuditText = false,
    bool Accessibility = true,
    int TakeoverTimeout = AgentProfile.DefaultTakeoverTimeout,
    string? Keymap = null,
    PlacementMode Placement = PlacementMode.Maximize)
{
    public const string DefaultName = "default";

    public const int DefaultWidth = 1280;

    public const int DefaultHeight = 800;

    public const int DefaultTakeoverTimeout = 10;

    public const string FileName = "agent.toml";

    public AgentAllowlist Allow { get; init; } = Allow ?? AgentAllowlist.Empty;

    public IReadOnlyList<string> Approve { get; init; } = Approve ?? [];

    public string File => Path.Combine(Directory, FileName);

    public string Home => Path.Combine(Directory, "home");

    public string Logs => Path.Combine(Directory, "logs");

    public string Audit => Path.Combine(Directory, "audit");

    public string LockFile => Path.Combine(Directory, ".lock");

    public bool Gates(string action) =>
        action == AgentApprovals.LaunchUnlisted ? Ask || Approve.Contains(action) : Approve.Contains(action);

    public bool CanLaunchAnything => !Allow.IsEmpty || Gates(AgentApprovals.LaunchUnlisted);

    public string LaunchSummary => !CanLaunchAnything
        ? $"the agent can launch nothing: the allowlist in {File} is empty and [launch] ask is false"
        : Gates(AgentApprovals.LaunchUnlisted)
            ? "the allowlist starts at once; anything else waits for a person's approval"
            : "the allowlist starts at once; anything else is refused";
}
