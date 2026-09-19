using Basin.Diagnostics;

namespace Waylonia;

internal sealed class IconCache
{
    private readonly string _root;

    public IconCache(string root)
    {
        ArgumentException.ThrowIfNullOrEmpty(root);
        _root = root;
    }

    public string Root => _root;

    public string DirectoryFor(string session) => Path.Combine(_root, Safe(session));

    public string? Find(string session, string name)
    {
        var stem = Path.Combine(DirectoryFor(session), Safe(name));
        foreach (var extension in (string[])[".png", ".svg"])
        {
            if (File.Exists(stem + extension))
            {
                return stem + extension;
            }
        }

        return null;
    }

    public string? Store(string session, RemoteIcon icon, BasinLogger log)
    {
        ArgumentNullException.ThrowIfNull(icon);
        var directory = DirectoryFor(session);
        var path = Path.Combine(directory, Safe(icon.Name) + "." + icon.Extension);
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(path, icon.Bytes);
            return path;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            log.Warn($"the icon {icon.Name} of {session} could not be cached at {path}: {error.Message}");
            return null;
        }
    }

    public static string Safe(string text)
    {
        var builder = new System.Text.StringBuilder(text.Length);
        foreach (var c in text)
        {
            builder.Append(char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' or '+' or '@' ? c : '_');
        }

        return builder.Length == 0 ? "_" : builder.ToString();
    }
}
