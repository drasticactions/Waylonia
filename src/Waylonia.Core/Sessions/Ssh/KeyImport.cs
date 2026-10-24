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
            return $"'{name}' can't be the file name of a key. Rename the file and import it again.";
        }

        var path = Path.Combine(sshDirectory, name);
        if (File.Exists(path))
        {
            return $"You already imported a key named {name}. Remove that one from the ssh folder first, or rename the new one.";
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
            return $"Couldn't import {name}. {UiSentence.Of(error.Message)}";
        }

        return null;
    }
}
