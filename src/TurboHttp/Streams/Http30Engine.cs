using System;
using System.Buffers;
using System.Net.Http;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.Internal;
using TurboHttp.Protocol.RFC9114;
using TurboHttp.Streams.Stages.Decoding;
using TurboHttp.Streams.Stages.Encoding;

namespace TurboHttp.Streams;

public sealed class Http30Engine : IHttpProtocolEngine
{
    internal const long MaxBatchWeight = 65_536;

    public BidiFlow<HttpRequestMessage, IOutputItem, IInputItem, HttpResponseMessage, NotUsed> CreateFlow()
    {
        var requestEncoder = new Http3RequestEncoder(maxTableCapacity: 4096);

        return BidiFlow.FromGraph(GraphDsl.Create(b =>
        {
            var requestToFrame = b.Add(new Http30Request2FrameStage(requestEncoder));
            var frameEncoder = b.Add(new Http30EncoderStage());
            var frameDecoder = b.Add(new Http30DecoderStage());
            var streamDecoder = b.Add(new Http30StreamStage());
            var connection = b.Add(new Http30ConnectionStage());
            var encoderPreface = b.Add(new Http30QpackEncoderPrefaceStage());
            var merge = b.Add(new Merge<IOutputItem>(2));

            // Request path: HttpRequestMessage → frames → connection
            b.From(requestToFrame.OutFrame).To(connection.InApp);

            // QPACK encoder instruction path: instructions → preface → merge
            b.From(requestToFrame.OutEncoder).To(encoderPreface.Inlet);
            b.From(encoderPreface.Outlet).To(merge.In(1));

            // Inbound: partition decoder stream bytes from regular frames
            var partition = b.Add(new Partition<IInputItem>(2, ClassifyInputItem));
            var extractBytes = b.Add(
                Flow.Create<IInputItem>().Select(ExtractDecoderStreamBytes));
            var decoderStream = b.Add(new QpackDecoderStreamStage());
            var feedback = b.Add(new QpackDecoderFeedbackStage(requestEncoder.QpackEncoder));

            b.From(partition.Out(0)).To(frameDecoder.Inlet);
            b.From(partition.Out(1)).Via(extractBytes).Via(decoderStream).To(feedback);

            // Server path: bytes → frames → connection → stream assembly
            b.From(frameDecoder.Outlet).To(connection.InServer);
            b.From(connection.OutApp).To(streamDecoder.Inlet);

            // Outbound path: connection → frame encoder → batch → merge → network
            var batchFlow = b.Add(
                Flow.Create<IOutputItem>()
                    .BatchWeighted(
                        MaxBatchWeight,
                        item => item is DataItem d ? d.Length : 0L,
                        item => item,
                        BatchConsolidate));

            b.From(connection.OutServer).To(frameEncoder.Inlet);
            b.From(frameEncoder.Outlet).Via(batchFlow).To(merge.In(0));

            return new BidiShape<
                HttpRequestMessage,
                IOutputItem,
                IInputItem,
                HttpResponseMessage>(
                requestToFrame.In,
                merge.Out,
                partition.In,
                streamDecoder.Outlet);
        }));
    }

    internal static int ClassifyInputItem(IInputItem item)
    {
        return item is Http3InputTaggedItem { StreamType: InputStreamType.QpackDecoder } ? 1 : 0;
    }

    internal static ReadOnlyMemory<byte> ExtractDecoderStreamBytes(IInputItem item)
    {
        var tagged = (Http3InputTaggedItem)item;
        var data = (DataItem)tagged.Inner;
        return data.Memory.Memory[..data.Length];
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
