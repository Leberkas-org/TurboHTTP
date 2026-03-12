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
    private readonly int _initialWindowSize;

    public Http20Engine() : this(65535)
    {
    }

    public Http20Engine(int initialWindowSize)
    {
        _initialWindowSize = initialWindowSize;
    }

    public BidiFlow<HttpRequestMessage, ITransportItem, (IMemoryOwner<byte>, int), HttpResponseMessage,
        NotUsed> CreateFlow()
    {
        var requestEncoder = new Http2RequestEncoder();
        var windowSize = _initialWindowSize;

        return BidiFlow.FromGraph(GraphDsl.Create(b =>
        {
            var streamIdAllocator = b.Add(new StreamIdAllocatorStage());
            var requestToFrame = b.Add(new Request2FrameStage(requestEncoder));
            var frameEncoder = b.Add(new Http20EncoderStage());
            var frameDecoder = b.Add(new Http20DecoderStage());
            var streamDecoder = b.Add(new Http20StreamStage());
            var connection = b.Add(new Http20ConnectionStage(windowSize));

            var toDataItem = b.Add(Flow.Create<(IMemoryOwner<byte>, int)>()
                .Select(ITransportItem (x) => new DataItem(x.Item1, x.Item2)));

            b.From(streamIdAllocator.Outlet).To(requestToFrame.Inlet);
            b.From(requestToFrame.Outlet).To(connection.Inlet2);
            b.From(connection.Outlet2).To(frameEncoder.Inlet);
            b.From(frameEncoder.Outlet).To(toDataItem.Inlet);
            b.From(frameDecoder.Outlet).To(connection.Inlet1);
            b.From(connection.Outlet1).To(streamDecoder.Inlet);

            return new BidiShape<
                HttpRequestMessage,
                ITransportItem,
                (IMemoryOwner<byte>, int),
                HttpResponseMessage>(
                streamIdAllocator.Inlet,
                toDataItem.Outlet,
                frameDecoder.Inlet,
                streamDecoder.Outlet);
        }));
    }
}
