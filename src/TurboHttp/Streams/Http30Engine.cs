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
        // Disable QPACK dynamic table — no QPACK encoder instruction stream is wired yet,
        // so all headers must use static table + literal encoding only.
        var requestEncoder = new Http3RequestEncoder(maxTableCapacity: 0);

        return BidiFlow.FromGraph(GraphDsl.Create(b =>
        {
            var requestToFrame = b.Add(new Http30Request2FrameStage(requestEncoder));
            var frameEncoder = b.Add(new Http30EncoderStage());
            var frameDecoder = b.Add(new Http30DecoderStage());
            var streamDecoder = b.Add(new Http30StreamStage());
            var connection = b.Add(new Http30ConnectionStage());

            // Request path: HttpRequestMessage → frames → connection
            b.From(requestToFrame.Outlet).To(connection.InApp);

            // Server path: bytes → frames → connection → stream assembly
            b.From(frameDecoder.Outlet).To(connection.InServer);
            b.From(connection.OutApp).To(streamDecoder.Inlet);

            // Outbound path: connection → frame encoder → batch → network
            var batchFlow = b.Add(
                Flow.Create<IOutputItem>()
                    .BatchWeighted(
                        MaxBatchWeight,
                        item => item is DataItem d ? d.Length : 0L,
                        item => item,
                        BatchConsolidate));

            b.From(connection.OutServer).To(frameEncoder.Inlet);
            b.From(frameEncoder.Outlet).Via(batchFlow);

            return new BidiShape<
                HttpRequestMessage,
                IOutputItem,
                IInputItem,
                HttpResponseMessage>(
                requestToFrame.Inlet,
                batchFlow.Outlet,
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
