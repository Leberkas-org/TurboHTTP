using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHTTP.Internal;
using TurboHTTP.Streams.Stages;
using TurboHTTP.Streams.Stages.Decoding;

namespace TurboHTTP.Streams;

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
            var connection = b.Add(new Http11ConnectionStage(_maxPipelineDepth));

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

            b.From(connection.OutNetwork).Via(batchFlow);

            return new BidiShape<
                HttpRequestMessage,
                IOutputItem,
                IInputItem,
                HttpResponseMessage>(
                connection.InApp,
                batchFlow.Outlet,
                connection.InServer,
                connection.OutResponse);
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