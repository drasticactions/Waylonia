using Basin.Diagnostics;

namespace Waylonia.Tests;

internal sealed class LogCapture : IBasinLogSink, IDisposable
{
    private readonly IBasinLogSink? _previousSink = BasinLog.Sink;
    private readonly BasinLogLevel _previousLevel = BasinLog.Level;

    public LogCapture()
    {
        BasinLog.Sink = this;
        BasinLog.Level = BasinLogLevel.Trace;
    }

    public List<string> Lines { get; } = [];

    public void Write(BasinLogLevel level, string category, ReadOnlySpan<char> message)
    {
        lock (Lines)
        {
            Lines.Add($"{level}: {message}");
        }
    }

    public void Dispose()
    {
        BasinLog.Sink = _previousSink;
        BasinLog.Level = _previousLevel;
    }
}
