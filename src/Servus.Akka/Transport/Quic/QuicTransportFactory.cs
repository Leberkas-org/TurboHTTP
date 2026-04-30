using Akka;
using Akka.Actor;
using Akka.Streams.Dsl;

namespace Servus.Akka.Transport.Quic;

public sealed class QuicTransportFactory(IActorRef connectionManager) : ITransportFactory
{
    public Flow<ITransportOutbound, ITransportInbound, NotUsed> Create()
    {
        return Flow.FromGraph(new QuicConnectionStage(connectionManager));
    }
}