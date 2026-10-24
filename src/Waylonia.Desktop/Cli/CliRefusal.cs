namespace Waylonia.Cli;

/// <summary>
/// Writes a refusal of the command line to standard error as one <c>waylonia:</c> line.
/// </summary>
internal static class CliRefusal
{
    /// <summary>
    /// The program name that starts every line.
    /// </summary>
    public const string Program = "waylonia";

    /// <summary>
    /// Writes the message as one line.
    /// </summary>
    /// <param name="message">The message, a CLI fragment or a capitalized sentence.</param>
    /// <returns>Always 1, the exit status of a refused run.</returns>
    public static int Write(string message)
    {
        Console.Error.WriteLine(CliMessage.Line(Program, message));
        return 1;
    }
}
