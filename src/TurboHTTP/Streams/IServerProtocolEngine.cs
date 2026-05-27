using Akka;
using Akka.Streams.Dsl;
using Servus.Akka.Transport;
using TurboHTTP.Streams.Stages.Server;

namespace TurboHTTP.Streams;

internal interface IServerProtocolEngine
{
    BidiFlow<ITransportInbound, RequestContext, RequestContext, ITransportOutbound, NotUsed> CreateFlow(
        IServiceProvider? services = null);
}

