using Avalonia;
using Avalonia.Controls;

namespace Waylonia.UI;

/// <summary>
/// One row of a form in Kirigami's FormLayout shape: a right-aligned label in a column every row of
/// the enclosing <c>Grid.IsSharedSizeScope</c> shares, the field beside it, and the hint and the
/// problem under the field.
/// </summary>
internal sealed class FormField : ContentControl
{
    public static readonly StyledProperty<string?> LabelProperty =
        AvaloniaProperty.Register<FormField, string?>(nameof(Label));

    public static readonly StyledProperty<string?> HintProperty =
        AvaloniaProperty.Register<FormField, string?>(nameof(Hint));

    public static readonly StyledProperty<string?> ProblemProperty =
        AvaloniaProperty.Register<FormField, string?>(nameof(Problem));

    /// <summary>The text in the label column; empty leaves the column blank.</summary>
    public string? Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    /// <summary>A dimmed sentence under the field.</summary>
    public string? Hint
    {
        get => GetValue(HintProperty);
        set => SetValue(HintProperty, value);
    }

    /// <summary>What is wrong with the field's value, shown in the negative color; null hides it.</summary>
    public string? Problem
    {
        get => GetValue(ProblemProperty);
        set => SetValue(ProblemProperty, value);
    }
}
