using Basin.Ipc;

namespace Waylonia.Agent;

internal sealed class AgentInternalSink(IpcClientState state) : IIpcReplySink
{
    public bool IsOpen => true;

    public IpcClientState State { get; } = state;

    public IpcReply? Delivered { get; private set; }

    public void Deliver(IpcReply reply) => Delivered = reply;
}
