using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Waylonia.Sessions;

namespace Waylonia;

internal sealed class KeyImporter : IKeyPicker
{
    public async Task<IReadOnlyList<PickedKey>> PickAsync(TopLevel? top)
    {
        if (top?.StorageProvider is not { CanOpen: true } storage)
        {
            return [];
        }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import ssh keys",
            AllowMultiple = true,
        });
        var picked = new List<PickedKey>();
        foreach (var file in files)
        {
            picked.Add(new PickedKey(file.Name, await file.OpenReadAsync()));
        }

        return picked;
    }
}
