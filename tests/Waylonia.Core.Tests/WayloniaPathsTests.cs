using Xunit;

namespace Waylonia.Tests;

public sealed class WayloniaPathsTests
{
    [Fact]
    public void A_sandbox_keeps_the_editable_files_in_documents_and_the_rest_under_the_two_roots()
    {
        var documents = Path.Combine(Path.GetTempPath(), "app", "files");
        var stateRoot = Path.Combine(Path.GetTempPath(), "app", "state");
        var cacheRoot = Path.Combine(Path.GetTempPath(), "app", "cache");
        var paths = WayloniaPaths.Sandbox(documents, stateRoot, cacheRoot);

        Assert.Equal(Path.Combine(documents, "waylonia.toml"), paths.ConfigFile);
        Assert.Equal(Path.Combine(documents, "sessions"), paths.SessionsDirectory);
        Assert.Equal(Path.Combine(documents, "ssh"), paths.SshDirectory);
        Assert.Equal(Path.Combine(stateRoot, "waylonia", "shell.toml"), paths.StateFile);
        Assert.Equal(Path.Combine(cacheRoot, "waylonia", "icons"), paths.IconCacheRoot);
        Assert.False(paths.ConfigExplicit);
        Assert.True(paths.HasConfig);
    }

    [Fact]
    public void The_apple_shaped_call_keeps_the_ios_files_where_they_were()
    {
        var documents = Path.Combine(Path.GetTempPath(), "app", "Documents");
        var library = Path.Combine(Path.GetTempPath(), "app", "Library");
        var paths = WayloniaPaths.Sandbox(documents, Path.Combine(library, "Application Support"), Path.Combine(library, "Caches"));

        Assert.Equal(Path.Combine(documents, "waylonia.toml"), paths.ConfigFile);
        Assert.Equal(Path.Combine(documents, "sessions"), paths.SessionsDirectory);
        Assert.Equal(Path.Combine(documents, "ssh"), paths.SshDirectory);
        Assert.Equal(Path.Combine(library, "Application Support", "waylonia", "shell.toml"), paths.StateFile);
        Assert.Equal(Path.Combine(library, "Caches", "waylonia", "icons"), paths.IconCacheRoot);
        Assert.False(paths.ConfigExplicit);
        Assert.True(paths.HasConfig);
    }

    [Fact]
    public void The_xdg_layout_reads_the_home_variables()
    {
        var paths = WayloniaPaths.Xdg();
        Assert.EndsWith(Path.Combine("waylonia", "waylonia.toml"), paths.ConfigFile, StringComparison.Ordinal);
        Assert.EndsWith(Path.Combine("waylonia", "sessions"), paths.SessionsDirectory, StringComparison.Ordinal);
        Assert.EndsWith(".ssh", paths.SshDirectory, StringComparison.Ordinal);
    }
}
