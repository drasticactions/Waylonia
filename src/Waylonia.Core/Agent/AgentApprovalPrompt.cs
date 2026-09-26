namespace Waylonia.Agent;

internal sealed record AgentApprovalPrompt(
    string Heading,
    string? Subject,
    string Description,
    string DontAskAgain,
    string Method,
    string Arguments,
    TimeSpan Timeout);
