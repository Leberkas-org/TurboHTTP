using System.Buffers;
using System.Net.Http;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.Streams.Stages;

namespace TurboHttp.Streams;

public class Http30Engine : IHttpProtocolEngine
{
    public BidiFlow<HttpRequestMessage, ITransportItem, (IMemoryOwner<byte>, int), HttpResponseMessage,
        NotUsed> CreateFlow()
    {
        return BidiFlow.FromGraph(GraphDsl.Create(_ =>
        {
            // TODO: 
            return new BidiShape<
                HttpRequestMessage,
                ITransportItem,
                (IMemoryOwner<byte>, int),
                HttpResponseMessage>(
                Sink.Ignore<HttpRequestMessage>().Shape.Inlet,
                Source.Empty<ITransportItem>().Shape.Outlet,
                Sink.Ignore<(IMemoryOwner<byte>, int)>().Shape.Inlet,
                Source.Empty<HttpResponseMessage>().Shape.Outlet);
        }));
    }
}