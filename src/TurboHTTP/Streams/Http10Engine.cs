using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHTTP.Internal;
using TurboHTTP.Streams.Stages;
using TurboHTTP.Streams.Stages.Decoding;

namespace TurboHTTP.Streams;

public class Http10Engine : IHttpProtocolEngine
{
    public BidiFlow<HttpRequestMessage, IOutputItem, IInputItem, HttpResponseMessage, NotUsed> CreateFlow()
    {
        return BidiFlow.FromGraph(GraphDsl.Create(b =>
        {
            var connection = b.Add(new Http10ConnectionStage());

            var batchFlow = b.Add(
                Flow.Create<IOutputItem>()
                    .BatchWeighted(
                        Http11Engine.MaxBatchWeight,
                        item => item is NetworkBuffer d ? d.Length : 0L,
                        item => item,
                        Http11Engine.BatchConsolidate));

            b.From(connection.OutNetwork).Via(batchFlow);

            return new BidiShape<
                HttpRequestMessage,
                IOutputItem,
                IInputItem,
                HttpResponseMessage>(
                connection.InApp,
                batchFlow.Outlet,
                connection.InServer,
                connection.OutResponse);
        }));
    }
}