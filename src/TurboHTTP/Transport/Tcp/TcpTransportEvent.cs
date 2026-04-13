using TurboHTTP.Internal;
using TurboHTTP.Transport.Connection;

namespace TurboHTTP.Transport.Tcp;

internal readonly record struct LeaseAcquired(ConnectionLease Lease) : ITcpTransportEvent;

internal readonly record struct AcquisitionFailed(Exception Error) : ITcpTransportEvent;

internal readonly record struct InboundBatch(IInputItem[] Batch, int Count, int Gen) : ITcpTransportEvent;

internal readonly record struct InboundComplete(TlsCloseKind CloseKind, int Gen) : ITcpTransportEvent;

internal readonly record struct InboundPumpFailed(Exception Error) : ITcpTransportEvent;

internal readonly record struct OutboundWriteDone : ITcpTransportEvent;

internal readonly record struct OutboundWriteFailed(Exception Error) : ITcpTransportEvent;

internal readonly record struct FlushNextCompleted : ITcpTransportEvent;

internal interface ITcpTransportEvent;