using System.Buffers;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.Internal;
using TurboHttp.Protocol.RFC9114;
using TurboHttp.Streams.Stages.Decoding;
using TurboHttp.Streams.Stages.Encoding;
using TurboHttp.Streams.Stages.Routing;

namespace TurboHttp.Streams;

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
            // ── Request path: broadcast to encoder and FIFO correlation ──
            var broadcast = b.Add(new Broadcast<HttpRequestMessage>(2));
            var requestToFrame = b.Add(new Http30Request2FrameStage(requestEncoder));
            var correlation = b.Add(new Http30CorrelationStage());

            b.From(broadcast.Out(0)).To(requestToFrame.In);
            b.From(broadcast.Out(1)).To(correlation.In0);

            // ── Connection stage ──
            var connection = b.Add(new Http30ConnectionStage());
            b.From(requestToFrame.OutFrame).To(connection.InApp);

            // ── QPACK encoder instruction path ──
            var encoderPreface = b.Add(new Http30QpackEncoderPrefaceStage());
            b.From(requestToFrame.OutEncoder).To(encoderPreface.Inlet);

            // ── Outbound: connection → frame encoder → batch ──
            var frameEncoder = b.Add(new Http30EncoderStage());
            var batchFlow = b.Add(
                Flow.Create<IOutputItem>()
                    .BatchWeighted(
                        MaxBatchWeight,
                        item => item is DataItem d ? d.Length : 0L,
                        item => item,
                        BatchConsolidate));

            b.From(connection.OutServer).To(frameEncoder.Inlet);

            // Merge encoder output + QPACK instructions before control preface + demux
            var preDemuxMerge = b.Add(new Merge<IOutputItem>(2));
            b.From(frameEncoder.Outlet).Via(batchFlow).To(preDemuxMerge.In(0));
            b.From(encoderPreface.Outlet).To(preDemuxMerge.In(1));

            // ── Control stream preface → demux → merge back ──
            var controlPreface = b.Add(new Http30ControlStreamPrefaceStage());
            var demux = b.Add(new Http30StreamDemuxStage());
            var demuxMerge = b.Add(new Merge<IOutputItem>(3));

            b.From(preDemuxMerge.Out).Via(controlPreface).To(demux.In);
            b.From(demux.OutRequest).To(demuxMerge.In(0));
            b.From(demux.OutControl).To(demuxMerge.In(1));
            b.From(demux.OutEncoder).To(demuxMerge.In(2));

            // ── Inbound: partition decoder stream bytes from regular frames ──
            var partition = b.Add(new Partition<IInputItem>(2, ClassifyInputItem));
            var frameDecoder = b.Add(new Http30DecoderStage());
            var extractBytes = b.Add(
                Flow.Create<IInputItem>().Select(ExtractDecoderStreamBytes));
            var decoderStream = b.Add(new QpackDecoderStreamStage());
            var feedback = b.Add(new QpackDecoderFeedbackStage(requestEncoder.QpackEncoder));

            b.From(partition.Out(0)).To(frameDecoder.Inlet);
            b.From(partition.Out(1)).Via(extractBytes).Via(decoderStream).To(feedback);

            // ── Server response path: frames → connection → stream assembly → correlation ──
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
                demuxMerge.Out,
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
        var data = (DataItem)tagged.Inner;
        return data.Memory.Memory[..data.Length];
    }

    private static IOutputItem BatchConsolidate(IOutputItem accumulated, IOutputItem next)
    {
        if (accumulated is not DataItem accData || next is not DataItem nextData) return next;
        var totalLength = accData.Length + nextData.Length;
        var owner = MemoryPool<byte>.Shared.Rent(totalLength);
        accData.Memory.Memory[..accData.Length].CopyTo(owner.Memory);
        nextData.Memory.Memory[..nextData.Length].CopyTo(owner.Memory[accData.Length..]);
        accData.Memory.Dispose();
        nextData.Memory.Dispose();
        return new DataItem(owner, totalLength) { Key = accData.Key };
    }
}