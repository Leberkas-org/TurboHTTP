using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
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

    public BidiFlow<ITransportInbound, HttpRequestMessage, HttpResponseMessage, ITransportOutbound, NotUsed> CreateFlow()
    {
        return BidiFlow.FromGraph(GraphDsl.Create(b =>
        {
            var connection = b.Add(new Http30ServerConnectionStage(_options));

            return new BidiShape<
                ITransportInbound,
                HttpRequestMessage,
                HttpResponseMessage,
                ITransportOutbound>(
                connection.InNetwork,
                connection.OutRequest,
                connection.InResponse,
                connection.OutNetwork);
        }));
    }
}
