using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.Internal;
using TurboHttp.Streams.Stages.Decoding;
using TurboHttp.Streams.Stages.Encoding;
using TurboHttp.Streams.Stages.Routing;

namespace TurboHttp.Streams;

public class Http11Engine : IHttpProtocolEngine
{
    // Kept for unit tests and potential future re-use; not wired into CreateFlow().
    internal const long MaxBatchWeight = 65_536;
    internal const long MinItemWeight = MaxBatchWeight / 8;

    private readonly int _maxPipelineDepth;

    public Http11Engine(int maxPipelineDepth = 8)
    {
        _maxPipelineDepth = maxPipelineDepth;
    }

    public BidiFlow<HttpRequestMessage, IOutputItem, IInputItem, HttpResponseMessage, NotUsed> CreateFlow()
    {
        return BidiFlow.FromGraph(GraphDsl.Create(b =>
        {
            var encoder = b.Add(new Http11EncoderStage());
            var decoder = b.Add(new Http11DecoderStage());
            var correlation = b.Add(new Http1XCorrelationStage(_maxPipelineDepth));

            var requestBCast = b.Add(new Broadcast<HttpRequestMessage>(2));
            var signalMerge = b.Add(new MergePreferred<IOutputItem>(1));

            b.From(requestBCast.Out(0)).To(encoder.Inlet);
            b.From(requestBCast.Out(1)).To(correlation.InRequest);

            b.From(decoder.Outlet).To(correlation.InResponse);

            // BatchWeighted consolidates multiple small NetworkBuffers from the encoder
            // into fewer, larger writes before they hit the transport — reducing
            // Channel.WriteAsync + Socket.WriteAsync syscalls under pipelining.
            // Control signals (StreamAcquireItem, ConnectionReuseItem) have weight 0
            // and pass through immediately without batching.
            var batchFlow = b.Add(
                Flow.Create<IOutputItem>()
                    .BatchWeighted(
                        MaxBatchWeight,
                        item => item is NetworkBuffer d ? d.Length : 0L,
                        item => item,
                        BatchConsolidate));

            b.From(encoder.Outlet).Via(batchFlow).To(signalMerge.In(0));
            b.From(correlation.OutControl).To(signalMerge.Preferred);

            return new BidiShape<
                HttpRequestMessage,
                IOutputItem,
                IInputItem,
                HttpResponseMessage>(
                requestBCast.In,
                signalMerge.Out,
                decoder.Inlet,
                correlation.OutResponse);
        }));
    }

    internal static IOutputItem BatchConsolidate(IOutputItem accumulated, IOutputItem next)
    {
        if (accumulated is not NetworkBuffer acc || next is not NetworkBuffer nxt) return next;
        var totalLength = acc.Length + nxt.Length;

        // Fast path: if the accumulated buffer has enough capacity, append in-place (zero-alloc).
        if (acc.Capacity >= totalLength)
        {
            nxt.Memory.CopyTo(acc.FullMemory[acc.Length..]);
            nxt.Dispose();
            acc.Length = totalLength;
            return acc;
        }

        // Slow path: rent larger buffer and copy both.
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