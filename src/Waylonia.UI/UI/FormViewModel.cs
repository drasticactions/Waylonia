using CommunityToolkit.Mvvm.ComponentModel;

namespace Waylonia.UI;

internal abstract class FormViewModel : ObservableObject
{
    private readonly Dictionary<string, string> _problems = [];

    public IReadOnlyDictionary<string, string> Problems => _problems;

    protected abstract IReadOnlyList<string> ProblemProperties { get; }

    public string? ProblemFor(string field) => _problems.GetValueOrDefault(field);

    public void ClearProblems()
    {
        if (_problems.Count == 0)
        {
            return;
        }

        _problems.Clear();
        RaiseProblems();
    }

    public void Complain(string field, string message)
    {
        if (_problems.TryAdd(field, message))
        {
            RaiseProblems();
        }
    }

    protected bool HasProblems => _problems.Count > 0;

    protected static string? Blank(string? text) => text is { } value && value.Trim().Length > 0 ? value.Trim() : null;

    protected static int IndexOf(IReadOnlyList<string> choices, string value)
    {
        for (var i = 0; i < choices.Count; i++)
        {
            if (choices[i] == value)
            {
                return i;
            }
        }

        return -1;
    }

    private void RaiseProblems()
    {
        OnPropertyChanged(nameof(Problems));
        foreach (var property in ProblemProperties)
        {
            OnPropertyChanged(property);
        }
    }
}
