using Basin;

namespace Waylonia.Sessions;

internal static class KeyImport
{
    public static string? Import(string sshDirectory, string name, Stream content)
    {
        ArgumentException.ThrowIfNullOrEmpty(sshDirectory);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(content);
        if (name.Length == 0 || name is "." or ".." || name.IndexOfAny(['/', '\\', '\0']) >= 0)
        {
            return $"'{name}' is not a file name a key can have";
        }

        var path = Path.Combine(sshDirectory, name);
        if (File.Exists(path))
        {
            return $"a key named {name} is already imported; remove it in Files first";
        }

        try
        {
            Directory.CreateDirectory(sshDirectory);
            using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                content.CopyTo(file);
            }

            if (PlatformFacts.HasDescriptors)
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return $"{name} was not imported: {error.Message}";
        }

        return null;
    }
}
