using Avalonia.Controls;
using Waylonia.Sessions;

namespace Waylonia;

internal sealed class NullKeyPicker : IKeyPicker
{
    public static readonly NullKeyPicker Instance = new();

    public Task<IReadOnlyList<PickedKey>> PickAsync(TopLevel? top) => Task.FromResult<IReadOnlyList<PickedKey>>([]);
}
