using UIKit;

namespace Waylonia;

public static class Program
{
    public static void Main(string[] args)
    {
        var run = IosHead.BuildRun();
        WayloniaApp.Prepare(run, IosHead.Compose(run));
        UIApplication.Main(args, null, typeof(AppDelegate));
    }
}
