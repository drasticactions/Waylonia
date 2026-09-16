using System.Text;
using Basin.Diagnostics;
using Xunit;

namespace Waylonia.Tests;

public sealed class RemoteIconsTests
{
    private const string Mark = "";

    private const string End = "";

    private static string Record(string name, string extension, byte[] bytes) =>
        $"{Mark}{name}{Mark}{extension}\n{Convert.ToBase64String(bytes)}\n{End}\n";

    [Fact]
    public void The_script_lists_only_safe_names_once_and_searches_the_icon_directories()
    {
        var script = RemoteIcons.Script(["foot", "org.gnome.Calculator", "foot", "bad name;rm", "/usr/share/pixmaps/x.png"]);
        Assert.Contains("'foot' 'org.gnome.Calculator' '/usr/share/pixmaps/x.png'", script, StringComparison.Ordinal);
        Assert.DoesNotContain("rm", script.Split("for n in")[1], StringComparison.Ordinal);
        Assert.Contains("hicolor", script, StringComparison.Ordinal);
        Assert.Contains("pixmaps", script, StringComparison.Ordinal);
        Assert.Contains("48x48", script, StringComparison.Ordinal);
        Assert.Contains("base64", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Records_parse_into_icons_and_misses()
    {
        var png = Encoding.ASCII.GetBytes("PNG-BYTES");
        var output = Record("foot", "png", png)
            + $"{Mark}nothing{Mark}-\n"
            + Record("app", "svg", [1, 2, 3])
            + $"{Mark}bad{Mark}png\n!!notbase64!!\n{End}\n";
        var parsed = RemoteIcons.Parse(output);

        Assert.Equal(2, parsed.Icons.Count);
        Assert.Equal("foot", parsed.Icons[0].Name);
        Assert.Equal("png", parsed.Icons[0].Extension);
        Assert.Equal(png, parsed.Icons[0].Bytes);
        Assert.Equal("svg", parsed.Icons[1].Extension);
        Assert.Contains("nothing", parsed.Missing);
        Assert.Contains("bad", parsed.Missing);
    }

    [Fact]
    public void The_cache_stores_a_session_icon_under_a_safe_name_and_finds_it_again()
    {
        var root = Path.Combine(Path.GetTempPath(), $"waylonia-icons-{Guid.NewGuid():N}");
        try
        {
            var cache = new IconCache(root);
            Assert.Null(cache.Find("lab", "org.gnome.Calculator"));
            var path = cache.Store("lab", new RemoteIcon("/usr/share/pixmaps/x", "png", [9, 9]), BasinLogger.None);
            Assert.NotNull(path);
            Assert.Equal(Path.Combine(root, "lab", "_usr_share_pixmaps_x.png"), path);
            Assert.Equal(path, cache.Find("lab", "/usr/share/pixmaps/x"));
            Assert.Equal([9, 9], File.ReadAllBytes(path!));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
