using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.Internal;
using TurboHttp.Streams.Stages.Decoding;
using TurboHttp.Streams.Stages.Encoding;
using TurboHttp.Streams.Stages.Routing;

namespace TurboHttp.Streams;

public class Http10Engine : IHttpProtocolEngine
{
    private readonly int _maxPipelineDepth;

    public Http10Engine(int maxPipelineDepth = 8)
    {
        _maxPipelineDepth = maxPipelineDepth;
    }

    public BidiFlow<HttpRequestMessage, IOutputItem, IInputItem, HttpResponseMessage, NotUsed> CreateFlow()
    {
        return BidiFlow.FromGraph(GraphDsl.Create(b =>
        {
            var encoder = b.Add(new Http10EncoderStage());
            var decoder = b.Add(new Http10DecoderStage());
            var correlation = b.Add(new Http1XCorrelationStage(_maxPipelineDepth));

            var requestBCast = b.Add(new Broadcast<HttpRequestMessage>(2));
            var signalMerge = b.Add(new MergePreferred<IOutputItem>(1));

            b.From(requestBCast.Out(0)).To(encoder.Inlet);
            b.From(requestBCast.Out(1)).To(correlation.InRequest);

            var batchFlow = b.Add(
                Flow.Create<IOutputItem>()
                    .BatchWeighted(
                        Http11Engine.MaxBatchWeight,
                        item => item is NetworkBuffer d ? d.Length : 0L,
                        item => item,
                        Http11Engine.BatchConsolidate));

            b.From(encoder.Outlet).Via(batchFlow).To(signalMerge.In(0));
            b.From(correlation.OutControl).To(signalMerge.Preferred);

            b.From(decoder.Outlet).To(correlation.InResponse);

            return new BidiShape<
                HttpRequestMessage,
                IOutputItem,
                IInputItem,
                HttpResponseMessage>(
                requestBCast.In,
                signalMerge.Out,
                decoder.Inlet,
                correlation.OutResponse);
        }));
    }
}