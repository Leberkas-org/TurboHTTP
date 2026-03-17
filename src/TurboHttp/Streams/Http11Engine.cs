using System.Net.Http;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.IO.Stages;
using TurboHttp.Streams.Stages;

namespace TurboHttp.Streams;

public class Http11Engine : IHttpProtocolEngine
{
    public BidiFlow<HttpRequestMessage, IOutputItem, IInputItem, HttpResponseMessage, NotUsed> CreateFlow()
    {
        return BidiFlow.FromGraph(GraphDsl.Create(b =>
        {
            var encoder = b.Add(new Http11EncoderStage());
            var decoder = b.Add(new Http11DecoderStage());
            var correlation = b.Add(new Http1XCorrelationStage());

            var requestBCast = b.Add(new Broadcast<HttpRequestMessage>(2));

            b.From(requestBCast.Out(0)).To(encoder.Inlet);
            b.From(requestBCast.Out(1)).To(correlation.In0);

            b.From(decoder.Outlet).To(correlation.In1);

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