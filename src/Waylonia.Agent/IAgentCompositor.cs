using Basin.Shell.Nested;

namespace Waylonia.Agent;

internal interface IAgentCompositor
{
    bool Headless { get; }

    Task<T> RunAsync<T>(Func<NestedShell, T> work, CancellationToken cancel);

    Task<bool> NextFrameAsync(CancellationToken cancel);

    void Post(Action action);

    event Action? Damaged;
}
