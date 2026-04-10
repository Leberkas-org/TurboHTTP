using Akka;
using Akka.Actor;
using Akka.Streams.Dsl;
using TurboHTTP.Internal;
using TurboHTTP.Streams;

namespace TurboHTTP.Transport.Tcp;

/// <summary>
/// Transport factory for TCP/TLS connections (HTTP/1.0, HTTP/1.1, HTTP/2).
/// Encapsulates connection management and client options, creating a new
/// <see cref="TcpConnectionStage"/> on demand.
/// </summary>
internal sealed class TcpTransportFactory : ITransportFactory
{
    private readonly IActorRef _connectionManager;
    private readonly TurboClientOptions _clientOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="TcpTransportFactory"/> class.
    /// </summary>
    /// <param name="connectionManager">Actor reference for managing TCP connection lifecycle</param>
    /// <param name="clientOptions">Client configuration options</param>
    public TcpTransportFactory(IActorRef connectionManager, TurboClientOptions clientOptions)
    {
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        _clientOptions = clientOptions ?? throw new ArgumentNullException(nameof(clientOptions));
    }

    /// <summary>
    /// Creates a TCP transport stage for the given configuration.
    /// </summary>
    /// <returns>A flow wrapping a <see cref="TcpConnectionStage"/>.</returns>
    public Flow<IOutputItem, IInputItem, NotUsed> Create()
    {
        return Flow.FromGraph(new TcpConnectionStage(_connectionManager, _clientOptions));
    }
}
