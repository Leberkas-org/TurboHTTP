using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using Servus.Akka.Transport;
using TurboHTTP.Streams.Stages.Server;

namespace TurboHTTP.Streams;

internal sealed class Http10ServerEngine : IServerProtocolEngine
{
    public BidiFlow<ITransportInbound, HttpRequestMessage, HttpResponseMessage, ITransportOutbound, NotUsed> CreateFlow()
    {
        return BidiFlow.FromGraph(GraphDsl.Create(b =>
        {
            var connection = b.Add(new Http10ConnectionStage());

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

