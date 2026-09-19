namespace Waylonia.Sessions;

internal sealed record SshSecretPrompt(SshSecretKind Kind, string Destination, int Attempt, string? KeyPath = null);
