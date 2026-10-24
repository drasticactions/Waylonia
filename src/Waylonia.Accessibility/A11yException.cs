namespace Waylonia.Accessibility;

/// <summary>
/// An accessibility request that failed, with capitalized sentences that say what went wrong and what to do next.
/// </summary>
public sealed class A11yException : Exception
{
    /// <summary>
    /// Creates the exception.
    /// </summary>
    /// <param name="message">The sentences.</param>
    public A11yException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Creates the exception around the failure that caused it.
    /// </summary>
    /// <param name="message">The sentences.</param>
    /// <param name="inner">The failure that caused it.</param>
    public A11yException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
