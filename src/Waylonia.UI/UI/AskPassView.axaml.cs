using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using AvaUnion.Controls;
using AvaUnion.Presenters;

namespace Waylonia.UI;

internal sealed partial class AskPassView : UserControl
{
    public const int DefaultWidth = 420;

    public const int DefaultHeight = 170;

    private const char Mask = '•';

    private readonly TextBox _secret;
    private readonly AskPassKind _kind;
    private bool _finished;

    public AskPassView()
        : this("Password:", AskPassKind.Password)
    {
    }

    public AskPassView(string prompt, AskPassKind kind)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        AvaloniaXamlLoader.Load(this);
        _kind = kind;
        this.FindControl<TextBlock>("PromptText")!.Text = prompt;
        var field = this.FindControl<ActionTextField>("SecretBox")!;
        _secret = field;
        var reveal = new TextFieldAction { IconName = "password-show-on", Text = "Show password" };
        reveal.Triggered += (_, _) =>
        {
            var hidden = _secret.PasswordChar == default;
            _secret.PasswordChar = hidden ? Mask : default;
            reveal.IconName = hidden ? "password-show-on" : "password-show-off";
            reveal.Text = hidden ? "Show password" : "Hide password";
        };
        field.RightActions.Add(reveal);
        var no = this.FindControl<Button>("NoButton")!;
        var yes = this.FindControl<Button>("YesButton")!;
        var cancel = this.FindControl<Button>("CancelButton")!;
        var ok = this.FindControl<Button>("OkButton")!;
        var yesNo = kind == AskPassKind.YesNo;
        this.FindControl<UnionIcon>("PromptIcon")!.IconName = yesNo ? "dialog-question" : "dialog-password";
        _secret.IsVisible = !yesNo;
        no.IsVisible = yesNo;
        yes.IsVisible = yesNo;
        cancel.IsVisible = !yesNo;
        ok.IsVisible = !yesNo;
        no.Click += (_, _) => Finish("no");
        yes.Click += (_, _) => Finish("yes");
        cancel.Click += (_, _) => Finish(null);
        ok.Click += (_, _) => Finish(_secret.Text ?? string.Empty);
        _secret.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Finish(_secret.Text ?? string.Empty);
                e.Handled = true;
            }
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Finish(null);
                e.Handled = true;
            }
        };
        AttachedToVisualTree += (_, _) => FocusInput();
    }

    public event Action<string?>? Answered;

    public void FocusInput()
    {
        if (_kind == AskPassKind.Password)
        {
            _secret.Focus();
        }
        else
        {
            Focus();
        }
    }

    public void Finish(string? answer)
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        Answered?.Invoke(answer);
    }
}
