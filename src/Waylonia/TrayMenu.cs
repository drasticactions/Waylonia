using Avalonia.Controls;

namespace Waylonia;

internal sealed class TrayMenu(bool applications, Action refresh, Action<string, string> launch, Action quit)
{
    public NativeMenu Menu { get; private set; } = new();

    public event Action<NativeMenu>? Rebuilt;

    public void ShowNotice(string text) => Rebuild([new NativeMenuItem(text) { IsEnabled = false }]);

    public void ShowApplications(IReadOnlyList<ApplicationMenuItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
        {
            ShowNotice("No applications found");
            return;
        }

        Rebuild(items.Select(Convert));
    }

    public void ShowQuitOnly() => Rebuild([]);

    private void Rebuild(IEnumerable<NativeMenuItemBase> top)
    {
        var menu = new NativeMenu();
        if (applications)
        {
            foreach (var item in top)
            {
                menu.Items.Add(item);
            }

            menu.Items.Add(new NativeMenuItemSeparator());
            var refreshItem = new NativeMenuItem("Refresh applications");
            refreshItem.Click += (_, _) => refresh();
            menu.Items.Add(refreshItem);
            menu.Items.Add(new NativeMenuItemSeparator());
        }

        var quitItem = new NativeMenuItem("Quit Waylonia");
        quitItem.Click += (_, _) => quit();
        menu.Items.Add(quitItem);
        Menu = menu;
        Rebuilt?.Invoke(menu);
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
        else if (item.Command is { } command)
        {
            var label = item.Label;
            native.Click += (_, _) => launch(label, command);
        }
        else
        {
            native.IsEnabled = false;
        }

        return native;
    }
}
