using Basin;
using Basin.Capabilities;
using Basin.Diagnostics;

namespace Waylonia.Agent;

internal sealed class AgentShots : IToplevelCommitObserver
{
    private const int QuietMs = 150;
    private const int CapMs = 500;

    private readonly IScreenCapture _capture;
    private readonly IOutput _output;
    private readonly ICompositorEventLoop _loop;
    private readonly IToplevelModel? _model;
    private readonly AgentAudit _audit;
    private readonly List<Action> _waiting = [];
    private IEventSource? _quiet;
    private IEventSource? _cap;

    public AgentShots(IScreenCapture capture, IOutput output, ICompositorEventLoop loop, IToplevelModel? model, AgentAudit audit)
    {
        _capture = capture;
        _output = output;
        _loop = loop;
        _model = model;
        _audit = audit;
    }

    public string? Take(string kind)
    {
        var source = CaptureSource.Output(_output);
        if (!_capture.Supports(source) || !_capture.TryDescribe(source, out var format) || format.Width <= 0 || format.Height <= 0)
        {
            return null;
        }

        var buffer = new MemoryBuffer(format.Width, format.Height, format.Format);
        try
        {
            if (!_capture.Capture(source, new Box(0, 0, format.Width, format.Height), buffer))
            {
                return null;
            }

            var name = _audit.NextShot(kind);
            _audit.Shot(name, BufferCapture.ReadRgba(buffer), buffer.Width, buffer.Height);
            return name;
        }
        finally
        {
            buffer.Destroy();
        }
    }

    public void TakeWhenQuiet(Action<string?> done)
    {
        ArgumentNullException.ThrowIfNull(done);
        _waiting.Add(() => done(Take("after")));
        if (_waiting.Count > 1)
        {
            return;
        }

        _model?.AddCommitObserver(this);
        _quiet = _loop.AddTimer(Settle);
        _quiet.UpdateTimer(QuietMs);
        _cap = _loop.AddTimer(Settle);
        _cap.UpdateTimer(CapMs);
    }

    public void OnToplevelCommitted(ulong toplevelId, in Box damage) => _quiet?.UpdateTimer(QuietMs);

    public void Flush() => Settle();

    private void Settle()
    {
        Stop();
        var waiting = _waiting.ToArray();
        _waiting.Clear();
        foreach (var action in waiting)
        {
            action();
        }
    }

    private void Stop()
    {
        _model?.RemoveCommitObserver(this);
        _quiet?.Remove();
        _quiet = null;
        _cap?.Remove();
        _cap = null;
    }
}
