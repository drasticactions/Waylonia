using Waylonia.Sessions;

namespace Waylonia.Tests.Ssh;

internal sealed class FakePrompter : ISshPrompter
{
    public List<SshSecretPrompt> Secrets { get; } = [];

    public List<SshHostKeyPrompt> HostKeys { get; } = [];

    public string? SecretAnswer { get; set; } = "hunter2";

    public bool HostKeyAnswer { get; set; } = true;

    public Task<string?> AskSecretAsync(SshSecretPrompt prompt, CancellationToken cancellation)
    {
        Secrets.Add(prompt);
        return Task.FromResult(SecretAnswer);
    }

    public Task<bool> ConfirmHostKeyAsync(SshHostKeyPrompt prompt, CancellationToken cancellation)
    {
        HostKeys.Add(prompt);
        return Task.FromResult(HostKeyAnswer);
    }
}
