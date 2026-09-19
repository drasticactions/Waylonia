using Avalonia.Logging;
using Basin.Diagnostics;

namespace Waylonia;

internal sealed class AvaloniaLogSink : ILogSink
{
    private readonly BasinLogger _log = BasinLog.For("avalonia-host");

    public bool IsEnabled(LogEventLevel level, string area) => level >= LogEventLevel.Warning;

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate) =>
        Write(level, $"{area}: {messageTemplate}");

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues) =>
        Write(level, $"{area}: {messageTemplate} [{string.Join(", ", propertyValues)}]");

    private void Write(LogEventLevel level, string text)
    {
        if (level >= LogEventLevel.Error)
        {
            _log.Error($"{text}");
        }
        else
        {
            _log.Warn($"{text}");
        }
    }
}
