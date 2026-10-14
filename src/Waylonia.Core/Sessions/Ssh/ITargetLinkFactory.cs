namespace Waylonia.Sessions;

internal interface ITargetLinkFactory : ISshLinkFactory
{
    bool IsAvailable => true;

    ISshLink Create(string destination, string? user, ISshPrompter prompter) => Create(destination, prompter);

    Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellation);
}
