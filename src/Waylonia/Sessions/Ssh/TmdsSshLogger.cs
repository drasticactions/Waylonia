using Basin.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Waylonia.Sessions;

internal sealed class TmdsSshLogger(BasinLogger log, string destination) : ILoggerFactory, ILogger
{
    private const int AuthenticatingEvent = 15;

    private const int AuthMethodFailedEvent = 21;

    private const int PartialSuccessEvent = 29;

    private volatile bool _authenticating;
    private volatile string? _allowedMethods;

    public bool Authenticating => _authenticating;

    public string? AllowedMethods => _allowedMethods;

    public ILogger CreateLogger(string categoryName) => this;

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        switch (eventId.Id)
        {
            case AuthenticatingEvent:
                _authenticating = true;
                break;
            case AuthMethodFailedEvent:
            case PartialSuccessEvent:
                _allowedMethods = MethodsOf(state) ?? _allowedMethods;
                break;
        }

        var level = logLevel <= LogLevel.Trace ? BasinLogLevel.Trace : BasinLogLevel.Debug;
        if (!log.IsEnabled(level))
        {
            return;
        }

        var text = formatter(state, exception);
        if (exception is not null && !text.Contains(exception.Message, StringComparison.Ordinal))
        {
            text = $"{text}: {exception.Message}";
        }

        if (level == BasinLogLevel.Trace)
        {
            log.Trace($"ssh {destination}: {text}");
        }
        else
        {
            log.Debug($"ssh {destination}: {text}");
        }
    }

    private static string? MethodsOf<TState>(TState state)
    {
        if (state is not IReadOnlyList<KeyValuePair<string, object?>> values)
        {
            return null;
        }

        foreach (var (key, value) in values)
        {
            if (key == "AllowedMethods" && value is System.Collections.IEnumerable methods)
            {
                var names = new List<string>();
                foreach (var method in methods)
                {
                    if (method?.ToString() is { Length: > 0 } name)
                    {
                        names.Add(name);
                    }
                }

                return string.Join(",", names);
            }
        }

        return null;
    }

    public void Dispose()
    {
    }
}
