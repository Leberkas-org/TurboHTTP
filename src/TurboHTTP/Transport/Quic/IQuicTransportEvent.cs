using TurboHTTP.Internal;
using TurboHTTP.Transport.Connection;

namespace TurboHTTP.Transport.Quic;

internal interface IQuicTransportEvent;

internal readonly record struct ConnectionLeaseAcquired(QuicConnectionLease Lease) : IQuicTransportEvent;

internal readonly record struct RequestLeaseAcquired(ConnectionLease Lease, long StreamId) : IQuicTransportEvent;

internal readonly record struct TypedLeaseAcquired(ConnectionLease Lease, long StreamTypeValue, long StreamId) : IQuicTransportEvent;

internal readonly record struct AcquisitionFailed(Exception Error) : IQuicTransportEvent;

internal readonly record struct InboundData(IInputItem Item, int Gen) : IQuicTransportEvent;

internal readonly record struct InboundComplete(QuicCloseKind CloseKind, int Gen, long StreamId) : IQuicTransportEvent;

internal readonly record struct InboundPumpFailed(Exception Error, long StreamId) : IQuicTransportEvent;

internal readonly record struct InboundStreamReady(QuicConnectionHandle.InboundStream Stream) : IQuicTransportEvent;

internal readonly record struct OutboundWriteDone : IQuicTransportEvent;

internal readonly record struct OutboundWriteFailed(Exception Error) : IQuicTransportEvent;

internal readonly record struct EarlyDataRejected(NetworkBuffer Buffer) : IQuicTransportEvent;

internal readonly record struct ConnectionMigrated(
    System.Net.EndPoint? OldLocalEndPoint,
    System.Net.EndPoint? NewLocalEndPoint) : IQuicTransportEvent;
