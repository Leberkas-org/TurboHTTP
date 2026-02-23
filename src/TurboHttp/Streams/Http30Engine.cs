using System.Buffers;
using System.Net.Http;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;

namespace TurboHttp.Streams;

public class Http30Engine : IHttpProtocolEngine
{
    public BidiFlow<HttpRequestMessage, (IMemoryOwner<byte>, int), (IMemoryOwner<byte>, int), HttpResponseMessage,
        NotUsed> CreateFlow()
    {
        return BidiFlow.FromGraph(GraphDsl.Create(_ =>
        {
            // TODO: 
            return new BidiShape<
                HttpRequestMessage,
                (IMemoryOwner<byte> buffer, int readableBytes),
                (IMemoryOwner<byte>buffer, int readableBytes),
                HttpResponseMessage>(
                Sink.Ignore<HttpRequestMessage>().Shape.Inlet,
                Source.Empty<(IMemoryOwner<byte>, int)>().Shape.Outlet,
                Sink.Ignore<(IMemoryOwner<byte>, int)>().Shape.Inlet,
                Source.Empty<HttpResponseMessage>().Shape.Outlet);
        }));
    }
}