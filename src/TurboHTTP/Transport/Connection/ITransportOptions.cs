namespace TurboHTTP.Transport.Connection;

public interface ITransportOptions
{
    string Host { get; init; }
    int Port { get; init; }
    TimeSpan ConnectTimeout { get; init; }
    int? SocketSendBufferSize { get; init; }
    int? SocketReceiveBufferSize { get; init; }
}