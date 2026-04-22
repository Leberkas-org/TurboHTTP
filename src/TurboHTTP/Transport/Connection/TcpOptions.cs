using System.Net;

namespace TurboHTTP.Transport.Connection;

/// <summary>
/// Configuration options for a plain TCP connection.
/// </summary>
internal record TcpOptions : ITransportOptions
{
    public required string Host { get; init; }
    public required int Port { get; init; }
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public int? SocketSendBufferSize { get; init; }
    public int? SocketReceiveBufferSize { get; init; }
    public bool UseProxy { get; init; }
    public IWebProxy? Proxy { get; init; }
    public ICredentials? DefaultProxyCredentials { get; init; }
}