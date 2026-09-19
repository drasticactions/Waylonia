using Waylonia.Cli;
using Xunit;

namespace Waylonia.Tests;

public sealed class TomlDocumentTests
{
    private static TomlDocument Parse(string text)
    {
        var document = TomlDocument.Parse(text, out var error);
        Assert.Null(error);
        return document!;
    }

    [Fact]
    public void Setting_keys_keeps_every_comment_and_unrelated_line()
    {
        var document = Parse("""
            # top
            #compress = "lz4"
            gpu = true # trail

            # before host
            [host]
            tray = true
            # inside host

            [hotkeys]
            "ctrl+alt+t" = "foot"
            # eof comment

            """);

        document.Root.Set("compress", "zstd");
        document.Root.Set("gpu", false);
        document.EnsureTable("host").Set("tray", false);
        document.EnsureTable("host").Set("clipboard", false);

        Assert.Equal("""
            # top
            #compress = "lz4"
            gpu = false # trail
            compress = "zstd"

            # before host
            [host]
            tray = false
            clipboard = false
            # inside host

            [hotkeys]
            "ctrl+alt+t" = "foot"
            # eof comment

            """, document.Render());
    }

    [Fact]
    public void Removing_a_key_moves_its_comments_to_the_next_line()
    {
        var document = Parse("# about a\na = 1\n# about b\nb = 2\n");
        Assert.True(document.Root.Remove("a"));
        Assert.Equal("# about a\n# about b\nb = 2\n", document.Render());

        Assert.True(document.Root.Remove("b"));
        Assert.Equal("# about a\n# about b\n", document.Render());
        Assert.False(document.Root.Remove("b"));
    }

    [Fact]
    public void A_comment_only_file_keeps_its_comments_above_the_new_keys()
    {
        var document = Parse("# doc\n#compress = \"lz4\"\n");
        document.Root.Set("compress", "none");
        document.EnsureTable("host").Set("tray", false);

        Assert.Equal("# doc\n#compress = \"lz4\"\n\ncompress = \"none\"\n\n[host]\ntray = false\n", document.Render());
    }

    [Fact]
    public void A_table_alone_in_a_comment_only_file_follows_the_comments()
    {
        var document = Parse("# doc\n");
        document.EnsureTable("hotkeys").Set("ctrl+alt+t", "foot");

        Assert.Equal("# doc\n\n[hotkeys]\n\"ctrl+alt+t\" = \"foot\"\n", document.Render());
        Assert.Equal(["ctrl+alt+t"], document.Table("hotkeys")!.Keys);
    }

    [Fact]
    public void A_dotted_table_is_listed_created_and_removed_with_its_comment_kept()
    {
        var document = Parse("# lab\n[desktops.lab]\nrecipe = \"sway\"\n\n# kde\n[desktops.kde]\nrecipe = \"plasma\"\n");
        Assert.Equal(["lab", "kde"], document.Tables("desktops"));

        document.EnsureTable("desktops", "cosmic").Set("recipe", "cosmic");
        Assert.Equal(["lab", "kde", "cosmic"], document.Tables("desktops"));

        Assert.True(document.RemoveTable("desktops", "lab"));
        Assert.Equal(
            "# lab\n\n# kde\n[desktops.kde]\nrecipe = \"plasma\"\n\n[desktops.cosmic]\nrecipe = \"cosmic\"\n",
            document.Render());
        Assert.False(document.RemoveTable("desktops", "lab"));

        Assert.True(document.RemoveTable("desktops", "cosmic"));
        Assert.Equal("# lab\n\n# kde\n[desktops.kde]\nrecipe = \"plasma\"\n", document.Render());
    }

    [Fact]
    public void Blank_lines_around_a_removed_node_do_not_pile_up()
    {
        var document = Parse("a = 1\n\nb = 2\n\nc = 3\n");
        Assert.True(document.Root.Remove("b"));
        Assert.Equal("a = 1\n\nc = 3\n", document.Render());

        var tables = Parse("[a]\nx = 1\n\n[b]\ny = 2\n\n[c]\nz = 3\n");
        Assert.True(tables.RemoveTable("b"));
        Assert.Equal("[a]\nx = 1\n\n[c]\nz = 3\n", tables.Render());
    }

    [Fact]
    public void Removing_the_last_table_keeps_the_comments_after_it()
    {
        var document = Parse("gpu = true\n\n[host]\ntray = false\n# the end\n");
        Assert.True(document.RemoveTable("host"));
        Assert.Equal("gpu = true\n\n# the end\n", document.Render());
        Assert.NotNull(TomlDocument.Parse(document.Render(), out _));
    }

    [Fact]
    public void Broken_text_reports_the_parse_error()
    {
        Assert.Null(TomlDocument.Parse("compress = \n", out var error));
        Assert.Contains("expecting a value", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Text_joins_a_string_array_and_reads_a_string()
    {
        var document = Parse("command = [\"tmux\", \"new\"]\nterminal = \"foot -e\"\ngpu = true\n");
        Assert.Equal("tmux new", document.Root.Text("command"));
        Assert.Equal("foot -e", document.Root.Text("terminal"));
        Assert.Null(document.Root.Text("gpu"));
        Assert.Null(document.Root.Text("missing"));
        Assert.True(document.Root.Has("gpu"));
    }

    [Fact]
    public void Replacing_a_value_keeps_the_comment_on_its_line()
    {
        var document = Parse("gpu = true # trail\nenv = [\"A=1\"] # list\n");
        document.Root.Set("gpu", false);
        document.Root.Set("env", ["B=2", "C=3"]);
        document.Root.Set("env", ["B=2", "C=3"]);

        Assert.Equal("gpu = false # trail\nenv = [\"B=2\", \"C=3\"] # list\n", document.Render());
    }

    [Fact]
    public void Appending_after_a_line_without_a_newline_and_before_tail_comments()
    {
        var document = Parse("gpu = true");
        document.Root.Set("audio", true);
        Assert.Equal("gpu = true\naudio = true\n", document.Render());

        var tailed = Parse("a = 1\n\n# tail\n");
        tailed.Root.Set("b", "two");
        Assert.Equal("a = 1\nb = \"two\"\n\n# tail\n", tailed.Render());
    }

    [Fact]
    public void A_new_table_after_a_table_without_a_final_newline_starts_on_its_own_line()
    {
        var document = Parse("[host]\ntray = false");
        document.EnsureTable("hotkeys").Set("ctrl+alt+t", "foot");
        Assert.Equal("[host]\ntray = false\n\n[hotkeys]\n\"ctrl+alt+t\" = \"foot\"\n", document.Render());
        Assert.NotNull(TomlDocument.Parse(document.Render(), out _));
    }

    [Fact]
    public void Numbers_write_as_integers_when_whole_and_stay_put_when_equal()
    {
        var document = Parse("size = 24 # px\nscale = 1.0\nfont = 13\n");
        document.Root.Set("size", 32L);
        document.Root.Set("scale", 1.0);
        document.Root.Set("font", 13.0);
        document.Root.Set("delay", 500L);
        document.Root.Set("ratio", 11.5);
        document.Root.Set("whole", 14.0);

        Assert.Equal(
            "size = 32 # px\nscale = 1.0\nfont = 13\ndelay = 500\nratio = 11.5\nwhole = 14\n",
            document.Render());
        Assert.NotNull(TomlDocument.Parse(document.Render(), out _));
    }

    [Fact]
    public void Strings_are_escaped_and_round_trip()
    {
        var document = TomlDocument.Empty();
        document.Root.Set("command", "say \"hi\" \\ there");
        var text = document.Render();
        Assert.Equal("say \"hi\" \\ there", Parse(text).Root.Text("command"));
    }
}
