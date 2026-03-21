using System.Net.Http;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.Internal;
using TurboHttp.Streams.Stages;
using TurboHttp.Streams.Stages.Decoding;
using TurboHttp.Streams.Stages.Encoding;

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

            b.From(requestBCast.Out(0)).To(encoder.Inlet);
            b.From(requestBCast.Out(1)).To(correlation.InRequest);

            var signalSink = b.Add(Sink.Ignore<IControlItem>().MapMaterializedValue(_ => NotUsed.Instance));
            b.From(correlation.OutSignal).To(signalSink);

            b.From(decoder.Outlet).To(correlation.InResponse);

            return new BidiShape<
                HttpRequestMessage,
                IOutputItem,
                IInputItem,
                HttpResponseMessage>(
                requestBCast.In,
                encoder.Outlet,
                decoder.Inlet,
                correlation.Out);
        }));
    }
}