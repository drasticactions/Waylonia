using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;

namespace Waylonia.Ui;

internal sealed class AskPassWindow : Window
{
    private readonly TextBox? _secret;

    public AskPassWindow(string prompt, AskPassKind kind)
    {
        Title = "Waylonia";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;
        ShowInTaskbar = true;
        var panel = new StackPanel { Margin = new Thickness(16), Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = prompt, TextWrapping = global::Avalonia.Media.TextWrapping.Wrap });
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
        };
        if (kind == AskPassKind.YesNo)
        {
            var no = new Button { Content = "No", IsCancel = true };
            no.Click += (_, _) => Finish("no");
            var yes = new Button { Content = "Yes", IsDefault = true };
            yes.Click += (_, _) => Finish("yes");
            buttons.Children.Add(no);
            buttons.Children.Add(yes);
        }
        else
        {
            _secret = new TextBox { PasswordChar = '•', PlaceholderText = "Password" };
            _secret.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    Finish(_secret.Text ?? string.Empty);
                    e.Handled = true;
                }
            };
            panel.Children.Add(_secret);
            var cancel = new Button { Content = "Cancel", IsCancel = true };
            cancel.Click += (_, _) => Close();
            var ok = new Button { Content = "OK", IsDefault = true };
            ok.Click += (_, _) => Finish(_secret.Text ?? string.Empty);
            buttons.Children.Add(cancel);
            buttons.Children.Add(ok);
        }

        panel.Children.Add(buttons);
        Content = panel;
        Opened += (_, _) => _secret?.Focus();
    }

    public string? Answer { get; private set; }

    private void Finish(string answer)
    {
        Answer = answer;
        Close();
    }
}
