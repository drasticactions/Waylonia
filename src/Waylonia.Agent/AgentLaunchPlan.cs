namespace Waylonia.Agent;

internal sealed record AgentLaunchPlan(IReadOnlyList<string> Argv, string Name, string? DesktopId);
