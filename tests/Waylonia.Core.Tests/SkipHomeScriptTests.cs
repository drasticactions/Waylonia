using System.Diagnostics;
using Basin.Freedesktop;
using Waylonia;
using Xunit;

namespace Waylonia.Tests;

public sealed class SkipHomeScriptTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("waylonia-skiphome-").FullName;

    private string Home => Path.Combine(_root, "home");

    private string System => Path.Combine(_root, "usr", "share");

    private string Flatpak => Path.Combine(Home, ".local", "share", "flatpak", "exports", "share");

    private void Plant()
    {
        Write(Path.Combine(Home, ".local", "share", "applications", "exported.desktop"), Entry("Exported"));
        Write(Path.Combine(Flatpak, "applications", "flat.desktop"), Entry("Flat"));
        Write(Path.Combine(System, "applications", "boxed.desktop"), Entry("Boxed"));
        Write(Path.Combine(Home, ".local", "share", "icons", "hicolor", "48x48", "apps", "exported.png"), "home");
        Write(Path.Combine(Home, ".icons", "hicolor", "48x48", "apps", "dot.png"), "dot");
        Write(Path.Combine(System, "icons", "hicolor", "48x48", "apps", "boxed.png"), "system");
    }

    private static string Entry(string name) => $"[Desktop Entry]\nType=Application\nName={name}\nExec={name.ToLowerInvariant()}\n";

    private static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private string Run(string script)
    {
        var info = new ProcessStartInfo("sh") { RedirectStandardOutput = true, UseShellExecute = false };
        info.ArgumentList.Add("-c");
        info.ArgumentList.Add(script);
        info.Environment.Clear();
        info.Environment["PATH"] = "/usr/bin:/bin";
        info.Environment["HOME"] = Home;
        info.Environment["XDG_DATA_DIRS"] = $"{Flatpak}:{System}";
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return output;
    }

    [Fact]
    public void Skipping_home_lists_only_what_the_container_installed()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "the scripts are POSIX sh");
        Plant();

        var all = RemoteApplications.Parse(Run(RemoteApplications.Script(DesktopLocale.None)), DesktopLocale.None);
        var boxed = RemoteApplications.Parse(Run(RemoteApplications.Script(DesktopLocale.None, skipHome: true)), DesktopLocale.None);

        Assert.Equal(["Boxed", "Exported", "Flat"], all.Entries.Select(static entry => entry.Name).Order());
        Assert.Equal(["Boxed"], boxed.Entries.Select(static entry => entry.Name));
    }

    [Fact]
    public void Skipping_home_fetches_no_icon_from_the_home_directory()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "the scripts are POSIX sh");
        Plant();

        var all = RemoteIcons.Parse(Run(RemoteIcons.Script(["exported", "dot", "boxed"])));
        var boxed = RemoteIcons.Parse(Run(RemoteIcons.Script(["exported", "dot", "boxed"], skipHome: true)));

        Assert.Equal(["boxed", "dot", "exported"], all.Icons.Select(static icon => icon.Name).Order());
        Assert.Equal(["boxed"], boxed.Icons.Select(static icon => icon.Name));
        Assert.Equal(["dot", "exported"], boxed.Missing.Order());
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
