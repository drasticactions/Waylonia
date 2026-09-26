namespace Waylonia.Accessibility;

/// <summary>
/// One accessible node as the service reports it.
/// </summary>
/// <param name="Id">The node id, <c>busname:objectpath</c>.</param>
/// <param name="Role">The lowercase AT-SPI role name with dashes, such as <c>push-button</c>.</param>
/// <param name="Name">The accessible name.</param>
/// <param name="States">The lowercase state names, such as <c>showing</c> and <c>enabled</c>.</param>
/// <param name="Box">The extents relative to the client area, or null when the node has none.</param>
/// <param name="Text">The Text interface content, truncated, or null when the node does not implement Text.</param>
public sealed record A11yNodeInfo(
    string Id,
    string Role,
    string Name,
    IReadOnlyList<string> States,
    A11yBox? Box,
    string? Text);
