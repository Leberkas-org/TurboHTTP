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
            var signalMerge = b.Add(new MergePreferred<IOutputItem>(1));

            b.From(streamIdAllocator.Outlet).To(requestToFrame.Inlet);
            b.From(requestToFrame.Outlet).To(connection.AppIn);
            b.From(connection.ServerOut).To(frameEncoder.Inlet);
            b.From(frameDecoder.Outlet).To(connection.ServerIn);
            b.From(connection.AppOut).To(streamDecoder.Inlet);

            var signalCast = b.Add(Flow.Create<IControlItem>().Select(IOutputItem (x) => x));

            b.From(frameEncoder.Outlet).To(signalMerge.In(0));
            b.From(connection.OutletSignal).Via(signalCast).To(signalMerge.Preferred);

            return new BidiShape<
                HttpRequestMessage,
                IOutputItem,
                IInputItem,
                HttpResponseMessage>(
                streamIdAllocator.Inlet,
                signalMerge.Out,
                frameDecoder.Inlet,
                streamDecoder.Outlet);
        }));
    }
}
