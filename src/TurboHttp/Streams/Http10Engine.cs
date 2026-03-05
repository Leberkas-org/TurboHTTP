using System.Buffers;
using System.Net.Http;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.Streams.Stages;

namespace TurboHttp.Streams;

public class Http10Engine : IHttpProtocolEngine
{
    public BidiFlow<HttpRequestMessage, (IMemoryOwner<byte>, int), (IMemoryOwner<byte>, int), HttpResponseMessage,
        NotUsed> CreateFlow()
    {
        return BidiFlow.FromGraph(GraphDsl.Create(b =>
        {
            var encoder = b.Add(new Http10EncoderStage());
            var decoder = b.Add(new Http10DecoderStage());
            var correlation = b.Add(new CorrelationHttp1XStage());

            var requestBCast = b.Add(new Broadcast<HttpRequestMessage>(2));

            b.From(requestBCast).To(encoder.Inlet);
            b.From(requestBCast).To(correlation.In0);

            b.From(decoder.Outlet).To(correlation.In1);

            return new BidiShape<
                HttpRequestMessage,
                (IMemoryOwner<byte>, int),
                (IMemoryOwner<byte>, int),
                HttpResponseMessage>(
                requestBCast.In,
                encoder.Outlet,
                decoder.Inlet,
                correlation.Out);
        }));
    }
}