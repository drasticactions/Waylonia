using Avalonia.Headless.XUnit;
using Basin.Diagnostics;
using Basin.Freedesktop;
using Xunit;

namespace Waylonia.Tests;

public sealed class BluecurveIconSourceTests
{
    [AvaloniaFact]
    public void Known_names_and_app_ids_extract_to_the_cache_once_and_unknown_ones_do_not()
    {
        var root = Path.Combine(Path.GetTempPath(), $"waylonia-bluecurve-{Guid.NewGuid():N}");
        try
        {
            var icons = new BluecurveIconSource(new IconCache(root), BasinLogger.None);
            var calculator = icons.Path("org.gnome.Calculator");
            Assert.NotNull(calculator);
            Assert.EndsWith("apps-icon-calculator.svg", calculator, StringComparison.Ordinal);
            Assert.True(File.Exists(calculator));
            Assert.Same(calculator, icons.Path("org.gnome.Calculator"));
            Assert.Equal(calculator, icons.ForWindow("org.gnome.Calculator", null));
            Assert.Equal(calculator, icons.ForWindow("com.example.Whatever", "gnome-calculator"));
            Assert.Null(icons.Path("no-such-icon-anywhere"));
            Assert.Null(icons.ForWindow("com.example.Nothing", null));
            Assert.NotNull(icons.Fallback());
            Assert.NotNull(icons.Path(BluecurveIconSource.QuitIcon));
            Assert.NotNull(icons.Path(BluecurveIconSource.PlaceIcon("~/Documents")));
            Assert.NotNull(icons.Path(BluecurveIconSource.CategoryIcon(DesktopMainCategory.Utility)));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [AvaloniaFact]
    public void An_entry_falls_back_from_its_icon_to_its_id_and_then_its_category()
    {
        var root = Path.Combine(Path.GetTempPath(), $"waylonia-bluecurve-{Guid.NewGuid():N}");
        try
        {
            var icons = new BluecurveIconSource(new IconCache(root), BasinLogger.None);
            var byIcon = new DesktopEntry { Id = "x.desktop", Path = "/x.desktop", Name = "X", Icon = "org.gnome.Calculator" };
            var byId = new DesktopEntry { Id = "gnome-calculator.desktop", Path = "/c.desktop", Name = "Calc", Icon = "not-in-bluecurve" };
            var byCategory = new DesktopEntry { Id = "z.desktop", Path = "/z.desktop", Name = "Z", Categories = ["Game"] };
            Assert.EndsWith("icon-calculator.svg", icons.ForEntry(byIcon)!, StringComparison.Ordinal);
            Assert.EndsWith("icon-calculator.svg", icons.ForEntry(byId)!, StringComparison.Ordinal);
            Assert.EndsWith("icon-games.svg", icons.ForEntry(byCategory)!, StringComparison.Ordinal);
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
