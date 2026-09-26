namespace Waylonia.Accessibility;

/// <summary>
/// An accessibility request that failed, with a lowercase sentence meant for the user.
/// </summary>
/// <param name="message">The sentence.</param>
public sealed class A11yException(string message) : Exception(message);
