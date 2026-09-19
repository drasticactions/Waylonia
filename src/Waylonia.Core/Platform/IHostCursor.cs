namespace Waylonia;

internal interface IHostCursor
{
    (int X, int Y)? TryGetPosition();

    void Close();
}
