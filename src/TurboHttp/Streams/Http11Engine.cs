using System.Buffers;
using System.Net.Http;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.Streams.Stages;

namespace TurboHttp.Streams;

public class Http11Engine : IHttpProtocolEngine
{
    public BidiFlow<HttpRequestMessage, ITransportItem, (IMemoryOwner<byte>, int), HttpResponseMessage,
        NotUsed> CreateFlow()
    {
        return BidiFlow.FromGraph(GraphDsl.Create(b =>
        {
            var encoder = b.Add(new Http11EncoderStage());
            var decoder = b.Add(new Http11DecoderStage());
            var correlation = b.Add(new Http1XCorrelationStage());

            var requestBCast = b.Add(new Broadcast<HttpRequestMessage>(2));

            var flow = b.Add(Flow.Create<(IMemoryOwner<byte>, int), ITransportItem>()
                .Select(ITransportItem (x) => new DataItem(x.Item1, x.Item2)));
            
            b.From(requestBCast.Out(0)).Via(encoder).To(flow.Inlet);
            b.From(requestBCast.Out(1)).To(correlation.In0);

            b.From(decoder.Outlet).To(correlation.In1);

            return new BidiShape<
                HttpRequestMessage,
                ITransportItem,
                (IMemoryOwner<byte>, int),
                HttpResponseMessage>(
                requestBCast.In,
                flow.Outlet,
                decoder.Inlet,
                correlation.Out);
        }));
    }
}