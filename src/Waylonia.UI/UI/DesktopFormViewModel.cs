using CommunityToolkit.Mvvm.ComponentModel;
using Waylonia.Cli;

namespace Waylonia.UI;

internal sealed partial class DesktopFormViewModel : FormViewModel
{
    public const string NameField = "name";

    public const string RecipeField = "recipe";

    public const string CommandField = "command";

    public const string SizeField = "size";

    public const string VideoField = "video";

    public const string NewLabel = "new desktop";

    public static IReadOnlyList<string> RecipeChoices { get; } =
        ["same as the name", .. DesktopRecipes.All.Select(static recipe => recipe.Name), "custom"];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Label))]
    private string _name = string.Empty;

    [ObservableProperty]
    private int _recipeIndex;

    [ObservableProperty]
    private string _host = string.Empty;

    [ObservableProperty]
    private string _size = string.Empty;

    [ObservableProperty]
    private string _command = string.Empty;

    [ObservableProperty]
    private string _env = string.Empty;

    [ObservableProperty]
    private bool? _gpu;

    [ObservableProperty]
    private string _video = string.Empty;

    public string Label => Name.Trim().Length == 0 ? NewLabel : Name.Trim();

    public string? NameProblem => ProblemFor(NameField);

    public string? RecipeProblem => ProblemFor(RecipeField);

    public string? CommandProblem => ProblemFor(CommandField);

    public string? SizeProblem => ProblemFor(SizeField);

    public string? VideoProblem => ProblemFor(VideoField);

    protected override IReadOnlyList<string> ProblemProperties { get; } =
    [
        nameof(NameProblem), nameof(RecipeProblem), nameof(CommandProblem), nameof(SizeProblem), nameof(VideoProblem),
    ];

    public static bool IsValidName(string name) => TomlKeys.IsBare(name);

    public void Load(DesktopProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        Name = profile.Name;
        RecipeIndex = Math.Max(0, profile.Recipe is null ? 0 : IndexOf(RecipeChoices, profile.Recipe));
        Host = profile.Host ?? string.Empty;
        Size = profile.Size ?? string.Empty;
        Command = profile.Command ?? string.Empty;
        Env = string.Join('\n', profile.Env);
        Gpu = profile.Gpu;
        Video = profile.Video ?? string.Empty;
        ClearProblems();
    }

    public DesktopProfile? Validate()
    {
        ClearProblems();
        var name = Name.Trim();
        if (name.Length == 0)
        {
            Complain(NameField, "Give the desktop a name.");
        }
        else if (!IsValidName(name))
        {
            Complain(NameField, "Use only letters, digits, '_' and '-' in the name.");
        }

        var recipe = RecipeIndex > 0 && RecipeIndex < RecipeChoices.Count ? RecipeChoices[RecipeIndex] : null;
        var command = Blank(Command);
        var effective = recipe ?? name;
        if (effective == "custom" && command is null)
        {
            Complain(CommandField, "A custom desktop needs the command that starts it.");
        }
        else if (recipe is null && name.Length > 0 && DesktopRecipes.Find(name) is null)
        {
            Complain(RecipeField, $"'{name}' is not a recipe. Pick {DesktopRecipes.Names}, or custom with a command.");
        }

        var size = Blank(Size);
        if (size is not null && RunRules.ParseSize(size) is null)
        {
            Complain(SizeField, "Use WIDTHxHEIGHT, such as 1920x1080.");
        }

        var video = Blank(Video);
        if (video is not null && !VideoChoice.IsValid(video))
        {
            Complain(VideoField, "Use none, h264, vp9 or av1. Add ,hw to decode on this host's GPU.");
        }

        if (HasProblems)
        {
            return null;
        }

        return new DesktopProfile(name, recipe, Blank(Host), size, command, HotkeyLines.Lines(Env), Gpu, video);
    }
}
