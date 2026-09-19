namespace Waylonia.Sessions;

internal interface ISshPrompter
{
    Task<string?> AskSecretAsync(SshSecretPrompt prompt, CancellationToken cancellation);

    Task<bool> ConfirmHostKeyAsync(SshHostKeyPrompt prompt, CancellationToken cancellation);
}
