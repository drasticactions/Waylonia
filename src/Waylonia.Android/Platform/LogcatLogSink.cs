using Basin.Diagnostics;

namespace Waylonia;

internal sealed class LogcatLogSink : IBasinLogSink
{
    private const string Tag = "waylonia";

    public void Write(BasinLogLevel level, string category, ReadOnlySpan<char> message)
    {
        var text = string.IsNullOrEmpty(category) ? message.ToString() : $"{category}: {message}";
        switch (level)
        {
            case BasinLogLevel.Trace:
            case BasinLogLevel.Debug:
                Android.Util.Log.Debug(Tag, text);
                break;
            case BasinLogLevel.Info:
                Android.Util.Log.Info(Tag, text);
                break;
            case BasinLogLevel.Warn:
                Android.Util.Log.Warn(Tag, text);
                break;
            case BasinLogLevel.Error:
                Android.Util.Log.Error(Tag, text);
                break;
            default:
                break;
        }
    }

    public void Flush()
    {
    }
}
