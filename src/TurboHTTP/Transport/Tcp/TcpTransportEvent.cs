using TurboHTTP.Internal;
using TurboHTTP.Transport.Connection;

namespace TurboHTTP.Transport.Tcp;

/// <summary>
/// Discriminated union of all async events that arrive from outside the stage thread.
/// Dispatched via <see cref="TcpTransportStateMachine.Dispatch"/>.
/// </summary>
internal abstract record TcpTransportEvent
{
    private TcpTransportEvent() { }

    internal sealed record LeaseAcquired(ConnectionLease Lease) : TcpTransportEvent;
    internal sealed record AcquisitionFailed(Exception Error) : TcpTransportEvent;
    internal sealed record InboundBatch(IInputItem[] Batch, int Count) : TcpTransportEvent;
    internal sealed record InboundComplete(TlsCloseKind CloseKind, int Gen) : TcpTransportEvent;
    internal sealed record InboundPumpFailed(Exception Error) : TcpTransportEvent;
    internal sealed record OutboundWriteDone : TcpTransportEvent;
    internal sealed record OutboundWriteFailed(Exception Error) : TcpTransportEvent;
    internal sealed record FlushNextCompleted : TcpTransportEvent;
}
