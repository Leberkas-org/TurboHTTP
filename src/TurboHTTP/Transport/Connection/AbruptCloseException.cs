namespace TurboHTTP.Transport.Connection;

/// <summary>
/// Signals that the transport connection was closed abruptly (no TLS close_notify, TCP RST, or I/O error).
/// Used to complete the inbound channel so that <see cref="TurboHTTP.Transport.Tcp.TcpConnectionStage"/> can distinguish
/// clean TLS closure from abrupt disconnection.
/// </summary>
public sealed class AbruptCloseException()
    : TurboTransportException("Connection closed abruptly without TLS close_notify");