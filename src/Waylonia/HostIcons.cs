using Basin.Freedesktop;

namespace Waylonia;

internal sealed class HostIcons
{
    private static readonly int[] PreferredSizes = [48, 64, 32, 96, 128, 256, 24, 22, 16];

    private readonly IconSearch _byName = new()
    {
        ReadDesktopEntry = false,
        Extensions = [".png", ".svg"],
        Sizes = PreferredSizes,
    };

    private readonly IconSearch _byAppId = new()
    {
        ReadDesktopEntry = true,
        Extensions = [".png", ".svg"],
        Sizes = PreferredSizes,
    };

    private readonly Dictionary<string, string?> _cache = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();

    public string? ForName(string? name) => name is { Length: > 0 } ? Lookup("n:" + name, () => _byName.Find(name)) : null;

    public string? ForAppId(string? appId) => appId is { Length: > 0 } ? Lookup("a:" + appId, () => _byAppId.Find(appId)) : null;

    private string? Lookup(string key, Func<string?> find)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out var known))
            {
                return known;
            }
        }

        var found = find();
        lock (_lock)
        {
            _cache[key] = found;
        }

        return found;
    }
}
