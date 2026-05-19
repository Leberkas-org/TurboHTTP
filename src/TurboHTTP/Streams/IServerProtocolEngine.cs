using Akka;
using Akka.Streams.Dsl;
using Servus.Akka.Transport;

namespace TurboHTTP.Streams;

internal interface IServerProtocolEngine
{
    BidiFlow<ITransportInbound, HttpRequestMessage, HttpResponseMessage, ITransportOutbound, NotUsed> CreateFlow();
}

