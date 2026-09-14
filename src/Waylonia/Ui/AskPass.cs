using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace Waylonia.Ui;

internal static class AskPass
{
    public const string Flag = "--askpass";

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
        environment["SSH_ASKPASS_REQUIRE"] = Require(Console.IsInputRedirected);
        if (OperatingSystem.IsLinux()
            && (!environment.TryGetValue("DISPLAY", out var display) || string.IsNullOrEmpty(display)))
        {
            environment["DISPLAY"] = "waylonia:0";
        }
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

        public override void Initialize() => Styles.Add(new global::Avalonia.Themes.Fluent.FluentTheme());

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
