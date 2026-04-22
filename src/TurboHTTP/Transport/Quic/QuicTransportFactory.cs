using Akka;
using Akka.Actor;
using Akka.Streams.Dsl;
using TurboHTTP.Internal;
using TurboHTTP.Streams;

#pragma warning disable CA1416

namespace TurboHTTP.Transport.Quic;

/// <summary>
/// Transport factory for QUIC connections (HTTP/3).
/// Mirrors <see cref="TurboHTTP.Transport.Tcp.TcpTransportFactory"/> — accepts a shared
/// <see cref="IActorRef"/> pointing to a <see cref="TurboHTTP.Transport.Connection.QuicConnectionManagerActor"/>.
/// </summary>
internal sealed class QuicTransportFactory(
    IActorRef connectionManager,
    bool allowConnectionMigration = true) : ITransportFactory
{
    /// <summary>
    /// Creates a QUIC transport stage wired to the shared connection manager actor.
    /// </summary>
    public Flow<IOutputItem, IInputItem, NotUsed> Create()
        => Flow.FromGraph(new QuicConnectionStage(connectionManager, allowConnectionMigration));
}
