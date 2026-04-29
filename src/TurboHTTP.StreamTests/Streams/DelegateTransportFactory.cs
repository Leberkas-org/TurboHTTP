using Akka;
using Akka.Streams.Dsl;
using Servus.Akka.Transport;

namespace TurboHTTP.StreamTests.Streams;

internal sealed class DelegateTransportFactory(Func<Flow<ITransportOutbound, ITransportInbound, NotUsed>> factory)
    : ITransportFactory
{
    public Flow<ITransportOutbound, ITransportInbound, NotUsed> Create() => factory();
}