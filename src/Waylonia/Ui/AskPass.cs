using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace Waylonia.Ui;

internal static class AskPass
{
    public const string Flag = "--askpass";

    public const string Variable = "WAYLONIA_ASKPASS";

    public static AskPassKind Classify(string prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        var trimmed = prompt.TrimEnd().TrimEnd('?', ':').TrimEnd();
        return trimmed.EndsWith("(yes/no)", StringComparison.OrdinalIgnoreCase)
            || trimmed.EndsWith("(yes/no/[fingerprint])", StringComparison.OrdinalIgnoreCase)
            ? AskPassKind.YesNo
            : AskPassKind.Password;
    }

    public static string Require(bool inputRedirected) => inputRedirected ? "force" : "prefer";

    public static void Configure(IDictionary<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        if (Environment.ProcessPath is not { Length: > 0 } self)
        {
            return;
        }

        environment["SSH_ASKPASS"] = self;
        environment[Variable] = "1";
        environment["SSH_ASKPASS_REQUIRE"] = Require(Console.IsInputRedirected);
        if (!environment.TryGetValue("DISPLAY", out var display) || string.IsNullOrEmpty(display))
        {
            environment["DISPLAY"] = "waylonia:0";
        }
    }

    public static bool IsAskPassRun(string[] args, string? variable) =>
        (args.Length >= 1 && args[0] == Flag) || variable == "1";

    public static string PromptOf(string[] args)
    {
        var words = args.Length >= 1 && args[0] == Flag ? args[1..] : args;
        return words.Length > 0 ? string.Join(' ', words) : "Password:";
    }

    public static int Run(string prompt)
    {
        AskPassApp.Prompt = prompt;
        var status = AppBuilder.Configure<AskPassApp>().UsePlatformDetect()
            .With(new MacOSPlatformOptions { ShowInDock = false })
            .StartWithClassicDesktopLifetime([]);
        if (status != 0 || AskPassApp.Answer is not { } answer)
        {
            return 1;
        }

        Console.Out.Write(answer);
        Console.Out.Flush();
        return 0;
    }

    private sealed class AskPassApp : Application
    {
        public static string Prompt { get; set; } = string.Empty;

        public static string? Answer { get; private set; }

        public override void Initialize() => Styles.Add(new global::BluerCurve.BluerCurveTheme());

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var window = new AskPassWindow(Prompt, Classify(Prompt));
                window.Closed += (_, _) => Answer = window.Answer;
                desktop.MainWindow = window;
                desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
