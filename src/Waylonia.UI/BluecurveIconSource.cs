using Basin.Diagnostics;
using Basin.Freedesktop;
using BluerCurve.Icons;

namespace Waylonia;

internal sealed class BluecurveIconSource
{
    public const string Session = "bluecurve";

    public const int Size = 48;

    private readonly IconCache _cache;
    private readonly BasinLogger _log;
    private readonly Dictionary<string, string?> _paths = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();

    public BluecurveIconSource(IconCache cache, BasinLogger log)
    {
        ArgumentNullException.ThrowIfNull(cache);
        _cache = cache;
        _log = log;
    }

    public static string? CategoryIcon(DesktopMainCategory category) => category switch
    {
        DesktopMainCategory.AudioVideo => "icon-audio",
        DesktopMainCategory.Development => "icon-development",
        DesktopMainCategory.Education => "icon-documentation",
        DesktopMainCategory.Game => "icon-games",
        DesktopMainCategory.Graphics => "icon-gfx",
        DesktopMainCategory.Network => "icon-internet",
        DesktopMainCategory.Office => "icon-office",
        DesktopMainCategory.Science => "icon-calculator",
        DesktopMainCategory.Settings => "icon-system-preferences",
        DesktopMainCategory.System => "icon-system",
        DesktopMainCategory.Utility => "icon-accessories",
        _ => "icon-applications",
    };

    public static string PlaceIcon(string path) => path switch
    {
        "~" => "folder-home",
        "~/Desktop" => "icon-desktop",
        "~/Documents" => "folder-documents",
        "~/Downloads" => "folder-downloads",
        "~/Pictures" => "folder-pictures",
        "~/Videos" => "folder-videos",
        "~/Music" => "folder-music",
        "trash:///" => "trash-empty",
        _ => "stock_folder",
    };

    public const string SessionsIcon = "icon-network";

    public const string SettingsIcon = "icon-system-preferences";

    public const string DisconnectIcon = "stock-disconnect";

    public const string QuitIcon = "stock-quit";

    public const string LauncherIcon = "icon-launcher";

    public const string KeyboardIcon = "icon-keyboard";

    public string? Path(string? name, string? context = null)
    {
        if (name is not { Length: > 0 })
        {
            return null;
        }

        var key = context is null ? name : $"{context}/{name}";
        lock (_lock)
        {
            if (_paths.TryGetValue(key, out var known))
            {
                return known;
            }
        }

        var path = Extract(name, context);
        lock (_lock)
        {
            _paths[key] = path;
        }

        return path;
    }

    public string? ForEntry(DesktopEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return Path(entry.Icon) ?? Path(Stem(entry.Id)) ?? Path(CategoryIcon(DesktopCategories.MainOf(entry)));
    }

    public string? ForWindow(string appId, string? clientIcon) =>
        Path(clientIcon) ?? Path(appId) ?? Path(Tail(appId));

    public string? Fallback() => Path(LauncherIcon);

    private static string Stem(string id) => id.EndsWith(".desktop", StringComparison.Ordinal) ? id[..^".desktop".Length] : id;

    private static string? Tail(string appId)
    {
        var dot = appId.LastIndexOf('.');
        return dot >= 0 && dot < appId.Length - 1 ? appId[(dot + 1)..].ToLowerInvariant() : null;
    }

    private string? Extract(string name, string? context)
    {
        BluecurveIcon? icon;
        try
        {
            icon = BluecurveIcons.Find(name, Size, context);
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or FormatException)
        {
            _log.Debug($"the Bluecurve icon set could not be read for {name}: {error.Message}");
            return null;
        }

        if (icon is null)
        {
            return null;
        }

        var stored = $"{icon.Context}-{icon.Name}";
        if (_cache.Find(Session, stored) is { } existing)
        {
            return existing;
        }

        try
        {
            using var stream = BluecurveIcons.Open(icon);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return _cache.Store(Session, new RemoteIcon(stored, "svg", memory.ToArray()), _log);
        }
        catch (Exception error) when (error is IOException or InvalidOperationException)
        {
            _log.Debug($"the Bluecurve icon {icon.Path} could not be extracted: {error.Message}");
            return null;
        }
    }
}
