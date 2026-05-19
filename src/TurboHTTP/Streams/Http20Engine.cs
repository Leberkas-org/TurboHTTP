using TurboHTTP.Client;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using Servus.Akka.Transport;
using TurboHTTP.Streams.Stages;

namespace TurboHTTP.Streams;

internal class Http20Engine(TurboClientOptions options) : IHttpProtocolEngine
{
    public BidiFlow<HttpRequestMessage, ITransportOutbound, ITransportInbound, HttpResponseMessage, NotUsed> CreateFlow()
    {
        return BidiFlow.FromGraph(GraphDsl.Create(b =>
        {
            var connection = b.Add(new Http20ConnectionStage(options));

            return new BidiShape<
                HttpRequestMessage,
                ITransportOutbound,
                ITransportInbound,
                HttpResponseMessage>(
                connection.InRequest,
                connection.OutNetwork,
                connection.InNetwork,
                connection.OutResponse);
        }));
    }
}