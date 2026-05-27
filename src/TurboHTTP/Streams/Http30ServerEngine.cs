using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using TurboHTTP.Server;
using TurboHTTP.Streams.Stages.Server;

namespace TurboHTTP.Streams;

internal sealed class Http30ServerEngine : IServerProtocolEngine
{
    private readonly TurboServerOptions _options;

    public Http30ServerEngine(TurboServerOptions options)
    {
        _options = options;
    }

    public Version ProtocolVersion => new(3, 0);

    public BidiFlow<ITransportInbound, IFeatureCollection, IFeatureCollection, ITransportOutbound, NotUsed> CreateFlow(IServiceProvider? services = null)
    {
        return BidiFlow.FromGraph(GraphDsl.Create(b =>
        {
            var connection = b.Add(new Http30ServerConnectionStage(_options, services));

            return new BidiShape<
                ITransportInbound,
                IFeatureCollection,
                IFeatureCollection,
                ITransportOutbound>(
                connection.InNetwork,
                connection.OutRequest,
                connection.InResponse,
                connection.OutNetwork);
        }));
    }
}
