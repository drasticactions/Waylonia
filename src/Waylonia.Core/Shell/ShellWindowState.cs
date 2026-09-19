namespace Waylonia.Shell;

internal sealed record ShellWindowState(int Width, int Height, int? X, int? Y, bool FullScreen)
{
    public const int DefaultWidth = 1280;

    public const int DefaultHeight = 800;

    public static ShellWindowState Default { get; } = new(DefaultWidth, DefaultHeight, null, null, false);
}
