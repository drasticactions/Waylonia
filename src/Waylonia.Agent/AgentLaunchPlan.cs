namespace Waylonia.Agent;

internal sealed record AgentLaunchPlan(IReadOnlyList<string> Argv, string Name, bool Listed, AgentAllowEntry? Entry, string? DesktopId);
