using Basin.Shell.Nested;

namespace Waylonia.Agent;

internal sealed record AgentProfile(
    string Name,
    string Directory,
    int Width = AgentProfile.DefaultWidth,
    int Height = AgentProfile.DefaultHeight,
    double Scale = 1.0,
    bool Headless = false,
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

    public IReadOnlyList<string> Approve { get; init; } = Approve ?? [];

    public string File => Path.Combine(Directory, FileName);

    public string Home => Path.Combine(Directory, "home");

    public string Logs => Path.Combine(Directory, "logs");

    public string Audit => Path.Combine(Directory, "audit");

    public string LockFile => Path.Combine(Directory, ".lock");

    public bool Gates(string action) => Approve.Contains(action);

    public string LaunchSummary => Gates(AgentApprovals.Launch)
        ? "every launch waits for a person's approval"
        : "any program starts at once";
}
