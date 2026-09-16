namespace Waylonia.Sessions;

internal sealed record SshHostKeyPrompt(string Host, int Port, string KeyType, string Fingerprint, SshHostKeyState State);
