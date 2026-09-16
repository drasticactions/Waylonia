using Avalonia.Headless.XUnit;
using Waylonia.Shell.Applets;
using Xunit;

namespace Waylonia.Tests.Shell;

public sealed class ClockViewModelTests
{
    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime();

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    [Fact]
    public void The_text_is_hours_and_minutes()
    {
        var time = new FixedTime(new DateTimeOffset(2026, 9, 16, 9, 5, 42, TimeSpan.Zero));
        var clock = new ClockViewModel(time);

        Assert.Equal("09:05", clock.Text);

        time.Now = new DateTimeOffset(2026, 9, 16, 23, 59, 0, TimeSpan.Zero);
        clock.Refresh();

        Assert.Equal("23:59", clock.Text);
    }

    [AvaloniaFact]
    public void Start_and_stop_own_the_timer()
    {
        var clock = new ClockViewModel();
        Assert.False(clock.IsRunning);

        clock.Start();
        clock.Start();
        Assert.True(clock.IsRunning);

        clock.Stop();
        Assert.False(clock.IsRunning);
    }
}
