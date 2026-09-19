using Avalonia.Headless.XUnit;
using Waylonia.UI;
using Xunit;

namespace Waylonia.Tests;

public sealed class AskPassViewTests
{
    [AvaloniaFact]
    public void The_view_answers_yes_no_and_a_password_and_cancels_once()
    {
        var yesNo = new AskPassView("Continue (yes/no)", AskPassKind.YesNo);
        string? answer = null;
        var answers = 0;
        yesNo.Answered += value =>
        {
            answer = value;
            answers++;
        };
        yesNo.Finish("yes");
        yesNo.Finish("no");
        Assert.Equal("yes", answer);
        Assert.Equal(1, answers);

        var password = new AskPassView("password:", AskPassKind.Password);
        string? secret = "unset";
        password.Answered += value => secret = value;
        password.Finish(null);
        Assert.Null(secret);
    }
}
