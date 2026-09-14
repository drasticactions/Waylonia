namespace Waylonia;

internal sealed record LocalDesktop(DesktopRecipe Recipe, IReadOnlyList<string> Env, (int Width, int Height)? Size, bool Gpu);
