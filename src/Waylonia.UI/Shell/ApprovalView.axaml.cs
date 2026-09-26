using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Waylonia.Agent;

namespace Waylonia.Shell;

internal sealed partial class ApprovalView : UserControl
{
    public const int DefaultWidth = 520;

    public const int DefaultHeight = 280;

    private readonly TextBlock _countdown;
    private readonly DateTime _deadline;
    private readonly Func<DateTime> _now;
    private DispatcherTimer? _timer;
    private bool _finished;

    public ApprovalView()
        : this(AgentApprovalText.For("windows/close", """{"id":1}""", TimeSpan.FromMinutes(2), window: "Untitled"))
    {
    }

    public ApprovalView(AgentApprovalPrompt prompt, Func<DateTime>? now = null)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        AvaloniaXamlLoader.Load(this);
        _now = now ?? (() => DateTime.UtcNow);
        _deadline = _now() + prompt.Timeout;
        this.FindControl<TextBlock>("HeadingText")!.Text = prompt.Heading;
        var subject = this.FindControl<SelectableTextBlock>("SubjectText")!;
        subject.Text = prompt.Subject;
        subject.IsVisible = prompt.Subject is { Length: > 0 };
        this.FindControl<TextBlock>("DescriptionText")!.Text = prompt.Description;
        this.FindControl<SelectableTextBlock>("DetailsText")!.Text = AgentApprovalText.Details(prompt);
        this.FindControl<Button>("YesRunButton")!.Content = prompt.DontAskAgain;
        _countdown = this.FindControl<TextBlock>("CountdownText")!;
        Tick();
        this.FindControl<Button>("NoButton")!.Click += (_, _) => Finish(AgentApprovalChoice.Deny);
        this.FindControl<Button>("YesRunButton")!.Click += (_, _) => Finish(AgentApprovalChoice.AllowRun);
        this.FindControl<Button>("YesButton")!.Click += (_, _) => Finish(AgentApprovalChoice.AllowOnce);
        Focusable = true;
        AddHandler(
            KeyDownEvent,
            (_, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    Finish(AgentApprovalChoice.Deny);
                    e.Handled = true;
                }
            },
            global::Avalonia.Interactivity.RoutingStrategies.Tunnel);
        AttachedToVisualTree += (_, _) =>
        {
            Focus();
            _timer ??= new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Tick());
            _timer.Start();
        };
        DetachedFromVisualTree += (_, _) => _timer?.Stop();
    }

    public event Action<AgentApprovalChoice>? Answered;

    public void Tick() => _countdown.Text = AgentApprovalText.Countdown(_deadline - _now());

    public void Finish(AgentApprovalChoice choice)
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        _timer?.Stop();
        Answered?.Invoke(choice);
    }
}
