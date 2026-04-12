using TurboHTTP.Internal;
using TurboHTTP.Transport.Connection;

namespace TurboHTTP.Transport.Quic;

/// <summary>
/// Discriminated union of all async events that arrive from outside the stage thread.
/// Dispatched via <see cref="QuicTransportStateMachine.Dispatch"/>.
/// </summary>
internal abstract record QuicTransportEvent
{
    private QuicTransportEvent() { }

    internal sealed record RequestLeaseAcquired(ConnectionLease Lease) : QuicTransportEvent;
    internal sealed record TypedLeaseAcquired(ConnectionLease Lease) : QuicTransportEvent;
    internal sealed record AcquisitionFailed(Exception Error) : QuicTransportEvent;
    internal sealed record InboundData(IInputItem Item) : QuicTransportEvent;
    internal sealed record InboundComplete(TlsCloseKind CloseKind, int Gen) : QuicTransportEvent;
    internal sealed record InboundPumpFailed(Exception Error) : QuicTransportEvent;
    internal sealed record InboundStreamReady(QuicConnectionManager.InboundStream Stream) : QuicTransportEvent;
    internal sealed record OutboundWriteDone : QuicTransportEvent;
    internal sealed record OutboundWriteFailed(Exception Error) : QuicTransportEvent;
}
