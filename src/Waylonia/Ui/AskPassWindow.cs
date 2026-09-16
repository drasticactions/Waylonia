using Avalonia.Controls;
using BluerCurve.Chrome;

namespace Waylonia.Ui;

internal sealed class AskPassWindow : BluerCurveWindow
{
    public AskPassWindow(string prompt, AskPassKind kind)
    {
        Title = "Waylonia";
        Width = AskPassView.DefaultWidth;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;
        ShowInTaskbar = true;
        var view = new AskPassView(prompt, kind);
        view.Answered += answer =>
        {
            Answer = answer;
            Close();
        };
        Content = view;
        Opened += (_, _) => view.FocusInput();
    }

    public string? Answer { get; private set; }
}
