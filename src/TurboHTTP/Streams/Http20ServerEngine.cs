using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using Servus.Akka.Transport;
using TurboHTTP.Server;
using TurboHTTP.Streams.Stages.Server;

namespace TurboHTTP.Streams;

internal sealed class Http20ServerEngine : IServerProtocolEngine
{
    private readonly TurboServerOptions _options;

    public Http20ServerEngine(TurboServerOptions options)
    {
        _options = options;
    }

    public BidiFlow<ITransportInbound, RequestContext, RequestContext, ITransportOutbound, NotUsed> CreateFlow(IServiceProvider? services = null)
    {
        return BidiFlow.FromGraph(GraphDsl.Create(b =>
        {
            var connection = b.Add(new Http20ServerConnectionStage(_options, services));

            return new BidiShape<
                ITransportInbound,
                RequestContext,
                RequestContext,
                ITransportOutbound>(
                connection.InNetwork,
                connection.OutRequest,
                connection.InResponse,
                connection.OutNetwork);
        }));
    }
}
