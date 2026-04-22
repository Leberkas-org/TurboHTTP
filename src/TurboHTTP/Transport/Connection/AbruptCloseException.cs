namespace TurboHTTP.Transport.Connection;

internal sealed class AbruptCloseException()
    : TurboTransportException("Connection closed abruptly without close_notify");