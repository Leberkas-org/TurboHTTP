using System.Buffers;
using System.Net.Http;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.Internal;
using TurboHttp.Protocol.RFC9113;
using TurboHttp.Streams.Stages.Features;
using TurboHttp.Streams.Stages.Decoding;
using TurboHttp.Streams.Stages.Encoding;
using TurboHttp.Streams.Stages.Routing;

namespace TurboHttp.Streams;

public class Http20Engine : IHttpProtocolEngine
{
    internal const long MaxBatchWeight = 65_536;

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
            var prependPreface = b.Add(new PrependPrefaceStage());
            var connection = b.Add(new Http20ConnectionStage(windowSize));
            var signalMerge = b.Add(new MergePreferred<IOutputItem>(1));

            b.From(streamIdAllocator.Outlet).To(requestToFrame.Inlet);
            b.From(requestToFrame.Outlet).To(connection.InApp);
            b.From(connection.OutServer).To(frameEncoder.Inlet);
            b.From(frameDecoder.Outlet).To(connection.InServer);
            b.From(connection.OutStream).To(streamDecoder.Inlet);

            var signalCast = b.Add(Flow.Create<IControlItem>().Select(IOutputItem (x) => x));

            var batchFlow = b.Add(
                Flow.Create<IOutputItem>()
                    .BatchWeighted(
                        MaxBatchWeight,
                        item => item is DataItem d ? d.Length : 0L,
                        item => item,
                        BatchConsolidate));

            b.From(frameEncoder.Outlet).Via(batchFlow).To(signalMerge.In(0));
            b.From(signalMerge.Out).To(prependPreface.Inlet);
            b.From(connection.OutSignal).Via(signalCast).To(signalMerge.Preferred);

            return new BidiShape<
                HttpRequestMessage,
                IOutputItem,
                IInputItem,
                HttpResponseMessage>(
                streamIdAllocator.Inlet,
                prependPreface.Outlet,
                frameDecoder.Inlet,
                streamDecoder.Outlet);
        }));
    }

    internal static IOutputItem BatchConsolidate(IOutputItem accumulated, IOutputItem next)
    {
        if (accumulated is DataItem accData && next is DataItem nextData)
        {
            var totalLength = accData.Length + nextData.Length;
            var owner = MemoryPool<byte>.Shared.Rent(totalLength);
            accData.Memory.Memory[..accData.Length].CopyTo(owner.Memory);
            nextData.Memory.Memory[..nextData.Length].CopyTo(owner.Memory.Slice(accData.Length));
            accData.Memory.Dispose();
            nextData.Memory.Dispose();
            return new DataItem(owner, totalLength) { Key = accData.Key };
        }

        return next;
    }
}