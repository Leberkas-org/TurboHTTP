using System.Buffers;
using System.Net.Http;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.IO.Stages;
using TurboHttp.Protocol.RFC9113;
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

    public BidiFlow<HttpRequestMessage, IOutputItem, IInputItem, HttpResponseMessage, NotUsed> CreateFlow()
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

            var flowOut = b.Add(Flow.Create<(IMemoryOwner<byte>, int), IOutputItem>()
                .Select(IOutputItem (x) => new DataItem(x.Item1, x.Item2)));
            var flowIn = b.Add(Flow.Create<IInputItem>().Where(x => x is DataItem).Select(x =>
            {
                var t = x as DataItem;
                
                return (t.Memory, t.Length);
            }));

            b.From(streamIdAllocator.Outlet).To(requestToFrame.Inlet);
            b.From(requestToFrame.Outlet).To(connection.Inlet2);
            b.From(connection.Outlet2).To(frameEncoder.Inlet);
            b.From(frameEncoder.Outlet).To(flowOut.Inlet);
            b.From(flowIn.Outlet).Via(frameDecoder).To(connection.Inlet1);
            b.From(connection.Outlet1).To(streamDecoder.Inlet);

            return new BidiShape<
                HttpRequestMessage,
                IOutputItem,
                IInputItem,
                HttpResponseMessage>(
                streamIdAllocator.Inlet,
                flowOut.Outlet,
                flowIn.Inlet,
                streamDecoder.Outlet);
        }));
    }
}