using System.Text;
using Waylonia.Sessions;
using Xunit;

namespace Waylonia.Tests;

public sealed class KeyImportTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "waylonia-import-" + Guid.NewGuid().ToString("n"));

    [Fact]
    public void A_key_is_copied_once_with_a_private_mode_and_a_second_copy_is_refused()
    {
        using var content = new MemoryStream(Encoding.ASCII.GetBytes("-----BEGIN OPENSSH PRIVATE KEY-----\n"));

        Assert.Null(KeyImport.Import(_directory, "id_ed25519", content));
        var path = Path.Combine(_directory, "id_ed25519");
        Assert.Equal("-----BEGIN OPENSSH PRIVATE KEY-----\n", File.ReadAllText(path));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
        }

        content.Position = 0;
        Assert.Contains("id_ed25519", KeyImport.Import(_directory, "id_ed25519", content), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("../id_rsa")]
    [InlineData("keys/id_rsa")]
    [InlineData("..")]
    [InlineData("")]
    public void A_name_with_a_path_in_it_is_refused(string name)
    {
        using var content = new MemoryStream();
        Assert.Contains($"'{name}'", KeyImport.Import(_directory, name, content), StringComparison.Ordinal);
        Assert.False(Directory.Exists(_directory) && Directory.EnumerateFileSystemEntries(_directory).Any());
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
