using Waylonia.Shell;
using Waylonia.Shell.Applets;
using Xunit;

namespace Waylonia.Tests.Shell;

public sealed class KeyboardViewModelTests
{
    [Fact]
    public void The_button_follows_the_model_and_toggles_through_the_commands()
    {
        var commands = new FakePanelCommands();
        var model = PanelModelFixture.Model(commands);
        model.Icons = new PanelIcons(Keyboard: "/icons/keyboard.svg");
        var keyboard = new KeyboardViewModel(model);

        Assert.False(keyboard.Open);
        Assert.Equal("/icons/keyboard.svg", keyboard.IconPath);

        model.SoftKeyboardOpen = true;
        Assert.True(keyboard.Open);

        model.Icons = new PanelIcons(Keyboard: "/icons/other.svg");
        Assert.Equal("/icons/other.svg", keyboard.IconPath);

        keyboard.ToggleCommand.Execute(null);
        Assert.Equal(["ToggleSoftKeyboard"], commands.Calls);
    }
}
