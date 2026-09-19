namespace Waylonia.Shell;

internal sealed record ShellKeyBinding(string Name, string Chord, HotkeyModifiers Modifiers, uint Code);
