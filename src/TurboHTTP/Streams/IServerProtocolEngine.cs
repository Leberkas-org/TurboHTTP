using Akka;
using Akka.Streams.Dsl;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;

namespace TurboHTTP.Streams;

internal interface IServerProtocolEngine
{
    Version ProtocolVersion { get; }

    BidiFlow<ITransportInbound, IFeatureCollection, IFeatureCollection, ITransportOutbound, NotUsed> CreateFlow(
        IServiceProvider? services = null);
}

