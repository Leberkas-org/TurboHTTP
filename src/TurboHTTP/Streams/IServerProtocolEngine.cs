using Akka;
using Akka.Streams.Dsl;
using Servus.Akka.Transport;
using TurboHTTP.Server;

namespace TurboHTTP.Streams;

internal interface IServerProtocolEngine
{
    BidiFlow<ITransportInbound, TurboHttpContext, TurboHttpContext, ITransportOutbound, NotUsed> CreateFlow();
}

