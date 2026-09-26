namespace Waylonia.Tests;

internal static class CompositorGate
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static IDisposable Enter()
    {
        if (!Gate.Wait(TimeSpan.FromMinutes(5)))
        {
            throw new TimeoutException("another test held the compositor for five minutes");
        }

        return new Release();
    }

    private sealed class Release : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                Gate.Release();
            }
        }
    }
}
