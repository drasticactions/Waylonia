using Basin;

namespace Waylonia;

internal static class VirtualInputGlobals
{
    public const string Keyboard = "zwp_virtual_keyboard_manager_v1";

    public const string Pointer = "zwlr_virtual_pointer_manager_v1";

    public static IReadOnlyList<string> Names { get; } = [Keyboard, Pointer];

    public static BasinServices Apply(BasinServices services, bool keep)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (keep)
        {
            return services;
        }

        foreach (var name in Names)
        {
            services = services.Without(name);
        }

        return services;
    }
}
