using Avalonia.Controls;
using Waylonia.Sessions;

namespace Waylonia;

internal interface IKeyPicker
{
    Task<IReadOnlyList<PickedKey>> PickAsync(TopLevel? top);
}
