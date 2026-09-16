using Basin.Diagnostics;

namespace Waylonia.Sessions;

internal sealed class TmdsSshLinkFactory(Func<TimeSpan> connectTimeout, BasinLogger log) : ISshLinkFactory
{
    private readonly SemaphoreSlim _prompts = new(1, 1);

    public ISshLink Create(string destination, ISshPrompter prompter)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(prompter);
        return new TmdsSshLink(destination, prompter, _prompts, connectTimeout(), log);
    }
}
