namespace Waylonia.Sessions;

internal interface ISshCommand : IDisposable
{
    Stream Output { get; }

    Stream Input { get; }

    IAsyncEnumerable<string> ErrorLines(CancellationToken cancellation);

    Task<int> Exited { get; }

    bool TrySignal(string name);
}
