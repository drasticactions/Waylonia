using Avalonia.Controls;

namespace Waylonia;

internal sealed class TrayMenu(Action<string?, string, string> launch, Action quit)
{
    public NativeMenu Menu { get; } = new();

    public void ShowNotice(string text) => Rebuild([new NativeMenuItem(text) { IsEnabled = false }, new NativeMenuItemSeparator()]);

    public void Show(IReadOnlyList<ApplicationMenuItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        Rebuild(items.Select(Convert));
    }

    public void ShowQuitOnly() => Rebuild([]);

    private void Rebuild(IEnumerable<NativeMenuItemBase> top)
    {
        var items = new List<NativeMenuItemBase>(top);
        while (items.Count > 0 && items[^1] is NativeMenuItemSeparator)
        {
            items.RemoveAt(items.Count - 1);
        }

        if (items.Count > 0)
        {
            items.Add(new NativeMenuItemSeparator());
        }

        var quitItem = new NativeMenuItem("Quit Waylonia");
        quitItem.Click += (_, _) => quit();
        items.Add(quitItem);
        Menu.Items.Clear();
        foreach (var item in items)
        {
            Menu.Items.Add(item);
        }
    }

    private NativeMenuItemBase Convert(ApplicationMenuItem item)
    {
        if (item.Separator)
        {
            return new NativeMenuItemSeparator();
        }

        var native = new NativeMenuItem(item.Label);
        if (item.Children is { } children)
        {
            var submenu = new NativeMenu();
            foreach (var child in children)
            {
                submenu.Items.Add(Convert(child));
            }

            native.Menu = submenu;
        }
        else if (item.Invoke is { } invoke)
        {
            native.Click += (_, _) => invoke();
        }
        else if (item.Command is { } command)
        {
            var label = item.Label;
            var session = item.Session;
            native.Click += (_, _) => launch(session, label, command);
        }
        else
        {
            native.IsEnabled = false;
        }

        return native;
    }
}
