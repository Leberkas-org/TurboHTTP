using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHTTP.Internal;
using TurboHTTP.Streams.Stages;

namespace TurboHTTP.Streams;

internal sealed class Http30Engine : IHttpProtocolEngine
{
    private readonly TurboClientOptions _options;

    public Http30Engine(TurboClientOptions options)
    {
        _options = options;
    }

    public BidiFlow<HttpRequestMessage, IOutputItem, IInputItem, HttpResponseMessage, NotUsed> CreateFlow()
    {
        return BidiFlow.FromGraph(GraphDsl.Create(b =>
        {
            var connection = b.Add(new Http30ConnectionStage(_options));

            return new BidiShape<
                HttpRequestMessage,
                IOutputItem,
                IInputItem,
                HttpResponseMessage>(
                connection.InApp,
                connection.OutNetwork,
                connection.InServer,
                connection.OutResponse);
        }));
    }
}