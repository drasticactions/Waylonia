using Basin;
using Basin.Capabilities;

namespace Waylonia.Shell;

internal sealed class ShellScreenSource : IUIScreenSource
{
    private Box _output;
    private double _scale;

    public ShellScreenSource(Box output, double scale)
    {
        _output = output;
        _scale = scale;
    }

    public int Count => 1;

    public event Action? Changed;

    public bool TryGet(int index, out UIScreenInfo info)
    {
        info = new UIScreenInfo(NestedShell.OutputKey, _output.X, _output.Y, _output.Width, _output.Height, _scale, true);
        return index == 0;
    }

    public void Update(Box output, double scale)
    {
        if (output == _output && Math.Abs(scale - _scale) < double.Epsilon)
        {
            return;
        }

        _output = output;
        _scale = scale;
        Changed?.Invoke();
    }
}
