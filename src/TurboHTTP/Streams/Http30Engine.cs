using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHTTP.Internal;
using TurboHTTP.Protocol.Http3;
using TurboHTTP.Streams.Stages.Decoding;
using TurboHTTP.Streams.Stages.Encoding;
using TurboHTTP.Streams.Stages.Routing;

namespace TurboHTTP.Streams;

public sealed class Http30Engine : IHttpProtocolEngine
{
    private const long MaxBatchWeight = 65_536;

    private readonly int _maxTableCapacity;

    public Http30Engine() : this(4096)
    {
    }

    public Http30Engine(int maxTableCapacity)
    {
        _maxTableCapacity = maxTableCapacity;
    }

    public BidiFlow<HttpRequestMessage, IOutputItem, IInputItem, HttpResponseMessage, NotUsed> CreateFlow()
    {
        var requestEncoder = new Http3RequestEncoder(maxTableCapacity: _maxTableCapacity);

        return BidiFlow.FromGraph(GraphDsl.Create(b =>
        {
            var broadcast = b.Add(new Broadcast<HttpRequestMessage>(2));
            var requestToFrame = b.Add(new Http30Request2FrameStage(requestEncoder));
            var correlation = b.Add(new Http30CorrelationStage());

            b.From(broadcast.Out(0)).To(requestToFrame.In);
            b.From(broadcast.Out(1)).To(correlation.In0);

            var connection = b.Add(new Http30ConnectionStage());
            b.From(requestToFrame.OutFrame).To(connection.InApp);

            var encoderPreface = b.Add(new Http30QpackEncoderPrefaceStage());
            b.From(requestToFrame.OutEncoder).To(encoderPreface.Inlet);

            var frameEncoder = b.Add(new Http30EncoderStage());
            var batchFlow = b.Add(
                Flow.Create<IOutputItem>()
                    .BatchWeighted(
                        MaxBatchWeight,
                        item => item is NetworkBuffer d ? d.Length : 0L,
                        item => item,
                        BatchConsolidate));

            b.From(connection.OutServer).To(frameEncoder.Inlet);

            // Merge encoder output + QPACK instructions before control preface + demux
            var preDemuxMerge = b.Add(new Merge<IOutputItem>(2));
            b.From(frameEncoder.Outlet).Via(batchFlow).To(preDemuxMerge.In(0));
            b.From(encoderPreface.Outlet).To(preDemuxMerge.In(1));

            var controlPreface = b.Add(new Http30ControlStreamPrefaceStage());

            b.From(preDemuxMerge.Out).To(controlPreface.Inlet);

            var partition = b.Add(new Partition<IInputItem>(2, ClassifyInputItem));
            var frameDecoder = b.Add(new Http30DecoderStage());
            var extractBytes = b.Add(
                Flow.Create<IInputItem>().Select(ExtractDecoderStreamBytes));
            var decoderStream = b.Add(new QpackDecoderStreamStage());
            var feedback = b.Add(new QpackDecoderFeedbackStage(requestEncoder.QpackEncoder));

            b.From(partition.Out(0)).To(frameDecoder.Inlet);
            b.From(partition.Out(1)).Via(extractBytes).Via(decoderStream).To(feedback);

            var streamDecoder = b.Add(new Http30StreamStage());
            b.From(frameDecoder.Outlet).To(connection.InServer);
            b.From(connection.OutApp).To(streamDecoder.Inlet);

            b.From(streamDecoder.Outlet).To(correlation.In1);

            return new BidiShape<
                HttpRequestMessage,
                IOutputItem,
                IInputItem,
                HttpResponseMessage>(
                broadcast.In,
                controlPreface.Outlet,
                partition.In,
                correlation.Out);
        }));
    }

    private static int ClassifyInputItem(IInputItem item)
    {
        return item is Http3InputTaggedItem { StreamType: InputStreamType.QpackDecoder } ? 1 : 0;
    }

    private static ReadOnlyMemory<byte> ExtractDecoderStreamBytes(IInputItem item)
    {
        var tagged = (Http3InputTaggedItem)item;
        var data = (NetworkBuffer)tagged.Inner;
        return data.Memory;
    }

    private static IOutputItem BatchConsolidate(IOutputItem accumulated, IOutputItem next)
    {
        if (accumulated is not NetworkBuffer acc || next is not NetworkBuffer nxt) return next;
        var totalLength = acc.Length + nxt.Length;
        if (acc.Capacity >= totalLength)
        {
            nxt.Memory.CopyTo(acc.FullMemory[acc.Length..]);
            nxt.Dispose();
            acc.Length = totalLength;
            return acc;
        }
        var merged = NetworkBuffer.Rent(totalLength);
        acc.Memory.CopyTo(merged.FullMemory);
        nxt.Memory.CopyTo(merged.FullMemory[acc.Length..]);
        acc.Dispose();
        nxt.Dispose();
        merged.Length = totalLength;
        merged.Key = acc.Key;
        return merged;
    }
}
