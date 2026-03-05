using System.Buffers;
using System.Net.Http;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.Streams.Stages;

namespace TurboHttp.Streams;

public class Http11Engine : IHttpProtocolEngine
{
    public BidiFlow<HttpRequestMessage, (IMemoryOwner<byte>, int), (IMemoryOwner<byte>, int), HttpResponseMessage,
        NotUsed> CreateFlow()
    {
        return BidiFlow.FromGraph(GraphDsl.Create(b =>
        {
            var encoder = b.Add(new Http11EncoderStage());
            var decoder = b.Add(new Http11DecoderStage());
            var correlation = b.Add(new CorrelationHttp1XStage());

            var requestBCast = b.Add(new Broadcast<HttpRequestMessage>(2));

            b.From(requestBCast.Out(0)).To(encoder.Inlet);
            b.From(requestBCast.Out(1)).To(correlation.In0);

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

public record HttpRequestOptions;

public record HttpRequest(HttpRequestOptions Options, HttpRequestMessage RequestMessage);

public class Http11EngineTest
{
    public BidiFlow<HttpRequest, IConnectionItem, (IMemoryOwner<byte>, int), HttpResponseMessage,
        NotUsed> CreateFlow()
    {
        return BidiFlow.FromGraph(GraphDsl.Create(b =>
        {
            var extractOptions = b.Add(new ExtractOptionsStage());
            var requestEncoder = b.Add(new Http11EncoderStage());
            var merge = b.Add(new Merge<IConnectionItem>(2));
            var responseDecoder = b.Add(new Http11DecoderStage());
            var wrapDataInput = b.Add(Flow.Create<(IMemoryOwner<byte>, int)>()
                .Select(IConnectionItem (chunk) => new DataInput(chunk.Item1, chunk.Item2)));

            // InitialInput (Out0) → merge
            b.From(extractOptions.Out0).To(merge);

            // HttpRequest (Out1) → Encoder → merge
            b.From(extractOptions.Out1).Via(requestEncoder).Via(wrapDataInput).To(merge);

            return new BidiShape<
                HttpRequest,
                IConnectionItem,
                (IMemoryOwner<byte>, int),
                HttpResponseMessage>(
                extractOptions.In,
                merge.Out,
                responseDecoder.Inlet,
                responseDecoder.Outlet);
        }));
    }
}