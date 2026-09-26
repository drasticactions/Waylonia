namespace Waylonia.Agent;

internal sealed class AgentCallException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
