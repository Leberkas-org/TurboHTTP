namespace Servus.Akka.Transport;

public interface ITransportInbound;

public sealed record TransportConnected(ConnectionInfo Info) : ITransportInbound;

public sealed record TransportDisconnected(DisconnectReason Reason) : ITransportInbound;

public sealed record TransportError(Exception Exception, bool Fatal) : ITransportInbound;

public sealed record StreamOpened(long StreamId, StreamDirection Direction) : ITransportInbound;

public sealed record StreamClosed(long StreamId, DisconnectReason Reason) : ITransportInbound;

public sealed record StreamReadCompleted(long StreamId) : ITransportInbound;

public sealed record ServerStreamAccepted(long StreamId, StreamDirection Direction) : ITransportInbound;

public sealed record InboundStreamAccepted(long StreamId, long StreamType) : ITransportInbound;

public sealed record ConnectionMigrationDetected(
    System.Net.EndPoint OldEndPoint,
    System.Net.EndPoint NewEndPoint) : ITransportInbound;
