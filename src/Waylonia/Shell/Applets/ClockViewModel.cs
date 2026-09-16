using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Waylonia.Shell.Applets;

internal sealed partial class ClockViewModel : ObservableObject
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    private readonly TimeProvider _time;

    private DispatcherTimer? _timer;

    [ObservableProperty]
    private string _text = string.Empty;

    public ClockViewModel(TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System;
        Refresh();
    }

    public bool IsRunning => _timer is not null;

    public static string Format(DateTimeOffset now) => now.ToString("HH:mm", CultureInfo.InvariantCulture);

    public void Refresh() => Text = Format(_time.GetLocalNow());

    public void Start()
    {
        if (_timer is not null)
        {
            return;
        }

        Refresh();
        _timer = new DispatcherTimer(Interval, DispatcherPriority.Background, (_, _) => Refresh());
        _timer.Start();
    }

    public void Stop()
    {
        _timer?.Stop();
        _timer = null;
    }
}
