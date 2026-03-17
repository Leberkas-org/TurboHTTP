using System.Net.Http;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.IO.Stages;
using TurboHttp.Streams.Stages;

namespace TurboHttp.Streams;

public class Http10Engine : IHttpProtocolEngine
{
    public BidiFlow<HttpRequestMessage, IOutputItem, IInputItem, HttpResponseMessage, NotUsed> CreateFlow()
    {
        return BidiFlow.FromGraph(GraphDsl.Create(b =>
        {
            var encoder = b.Add(new Http10EncoderStage());
            var decoder = b.Add(new Http10DecoderStage());
            var correlation = b.Add(new Http1XCorrelationStage());

            var requestBCast = b.Add(new Broadcast<HttpRequestMessage>(2));

            var flowIn = b.Add(Flow.Create<IInputItem>()
                .Select(x => (((DataItem)x).Memory, ((DataItem)x).Length)));

            b.From(requestBCast.Out(0)).Via(encoder);
            b.From(requestBCast.Out(1)).To(correlation.RequestIn);

            var signalSink = b.Add(Sink.Ignore<IControlItem>().MapMaterializedValue(_ => NotUsed.Instance));
            b.From(correlation.OutletSignal).To(signalSink);

            b.From(flowIn.Outlet).Via(decoder).To(correlation.ResponseIn);

            return new BidiShape<
                HttpRequestMessage,
                IOutputItem,
                IInputItem,
                HttpResponseMessage>(
                requestBCast.In,
                encoder.Outlet,
                flowIn.Inlet,
                correlation.Out);
        }));
    }
}