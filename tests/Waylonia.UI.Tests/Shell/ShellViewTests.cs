using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Waylonia.Shell;
using Xunit;

namespace Waylonia.Tests.Shell;

public sealed class ShellViewTests
{
    private static (Window Window, ShellView View, List<(int Width, int Height, double Scale)> Sizes) Show()
    {
        var view = new ShellView();
        var sizes = new List<(int, int, double)>();
        view.OutputResized += (width, height, scale) => sizes.Add((width, height, scale));
        var window = new Window { Width = 800, Height = 600, Content = view };
        window.Show();
        window.UpdateLayout();
        return (window, view, sizes);
    }

    [AvaloniaFact]
    public void The_output_is_the_stage_inside_the_safe_area()
    {
        var (window, view, sizes) = Show();
        Assert.Equal((800, 600, 1.0), sizes[^1]);

        view.ApplySafeArea(new Thickness(0, 24, 0, 20));
        window.UpdateLayout();
        Assert.Equal((800, 556, 1.0), sizes[^1]);
        Assert.Equal(new Thickness(0, 24, 0, 20), view.Padding);

        view.ApplySafeArea(new Thickness(44, 0, 44, 0));
        window.UpdateLayout();
        Assert.Equal((712, 600, 1.0), sizes[^1]);
        window.Close();
    }

    [AvaloniaFact]
    public void The_soft_keyboard_shortens_the_output_and_flips_the_open_flag()
    {
        var (window, view, sizes) = Show();
        var opened = new List<bool>();
        view.SoftKeyboardChanged += opened.Add;
        view.ApplySafeArea(new Thickness(0, 0, 0, 20));
        window.UpdateLayout();
        Assert.Equal((800, 580, 1.0), sizes[^1]);

        view.ApplyInputPane(InputPaneState.Open, new Rect(0, 350, 800, 250));
        window.UpdateLayout();
        Assert.Equal((800, 350, 1.0), sizes[^1]);
        Assert.True(view.SoftKeyboardOpen);
        Assert.Equal(250, view.KeyboardHeight);

        view.ApplyInputPane(InputPaneState.Closed, default);
        window.UpdateLayout();
        Assert.Equal((800, 580, 1.0), sizes[^1]);
        Assert.Equal([true, false], opened);
        window.Close();
    }

    [AvaloniaFact]
    public void Toggling_the_keyboard_asks_the_text_input_for_the_opposite_state()
    {
        var (window, view, _) = Show();
        var shown = new List<bool>();
        view.SoftKeyboard = shown.Add;

        view.ToggleSoftKeyboard();
        view.ApplyInputPane(InputPaneState.Open, new Rect(0, 400, 800, 200));
        view.ToggleSoftKeyboard();

        Assert.Equal([true, false], shown);
        Assert.Equal(Brushes.Black, view.Background);
        window.Close();
    }
}
