namespace Waylonia;

internal sealed class NullHostCursor : IHostCursor
{
    public static readonly NullHostCursor Instance = new();

    public (int X, int Y)? TryGetPosition() => null;

    public void Close()
    {
    }
}
