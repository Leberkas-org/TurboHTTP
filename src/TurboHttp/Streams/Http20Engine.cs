using System.Buffers;
using System.Net.Http;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.Protocol;
using TurboHttp.Streams.Stages;

namespace TurboHttp.Streams;

public class Http20Engine : IHttpProtocolEngine
{
    public BidiFlow<HttpRequestMessage, (IMemoryOwner<byte>, int), (IMemoryOwner<byte>, int), HttpResponseMessage,
        NotUsed> CreateFlow()
    {
        var requestEncoder = new Http2RequestEncoder();

        return BidiFlow.FromGraph(GraphDsl.Create(b =>
        {
            var streamIdAllocator = b.Add(new StreamIdAllocator());
            var requestToFrame = b.Add(new Request2FrameStage(requestEncoder));
            var frameEncoder = b.Add(new Http20EncoderStage());
            var prependPreface = b.Add(new PrependPrefaceStage());
            var frameDecoder = b.Add(new Http20DecoderStage());
            var streamDecoder = b.Add(new Http20StreamStage());
            var connection = b.Add(new Http20ConnectionStage());
            
            b.From(streamIdAllocator.Outlet).To(requestToFrame.Inlet);
            b.From(requestToFrame.Outlet).To(connection.Inlet2);
            b.From(connection.Outlet2).To(frameEncoder.Inlet);
            b.From(frameEncoder.Outlet).To(prependPreface.Inlet);
            b.From(frameDecoder.Outlet).To(connection.Inlet1);
            b.From(connection.Outlet1).To(streamDecoder.Inlet);

            return new BidiShape<
                HttpRequestMessage,
                (IMemoryOwner<byte> buffer, int readableBytes),
                (IMemoryOwner<byte> buffer, int readableBytes),
                HttpResponseMessage>(
                streamIdAllocator.Inlet,
                prependPreface.Outlet,
                frameDecoder.Inlet,
                streamDecoder.Outlet);
        }));
    }
}