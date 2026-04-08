using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHTTP.Internal;
using TurboHTTP.Protocol.Http2;
using TurboHTTP.Streams.Stages.Decoding;
using TurboHTTP.Streams.Stages.Encoding;

namespace TurboHTTP.Streams;

public class Http20Engine : IHttpProtocolEngine
{
    // Stateless cast flow — reused across all materializations to avoid per-call allocation.
    private static readonly Flow<IControlItem, IOutputItem, NotUsed> _signalCast =
        Flow.Create<IControlItem>().Select(IOutputItem (x) => x);
    internal const long DefaultMaxBatchWeight = 262_144;

    /// <summary>
    /// Default initial value for MAX_CONCURRENT_STREAMS before the server sends its SETTINGS.
    /// RFC 9113 §6.5.2: "Initially, there is no limit to this value."
    /// We use <see cref="int.MaxValue"/> to represent "unlimited".
    /// </summary>
    internal const int DefaultMaxConcurrentStreams = int.MaxValue;

    public Http20Engine() : this(1_048_576)
    {
    }

    public Http20Engine(int initialWindowSize, int maxConcurrentStreams = DefaultMaxConcurrentStreams)
        : this(initialWindowSize, maxConcurrentStreams, DefaultMaxBatchWeight)
    {
    }

    public Http20Engine(int initialWindowSize, int maxConcurrentStreams, long maxBatchWeight)
    {
        InitialWindowSize = initialWindowSize;
        MaxConcurrentStreams = maxConcurrentStreams;
        MaxBatchWeight = maxBatchWeight;
    }

    /// <summary>
    /// The configured initial MAX_CONCURRENT_STREAMS limit.
    /// This value is passed to the underlying <see cref="Http20ConnectionStage"/>
    /// and will be updated at runtime when the server sends a SETTINGS frame
    /// with <see cref="SettingsParameter.MaxConcurrentStreams"/>.
    /// </summary>
    public int MaxConcurrentStreams { get; }

    /// <summary>The configured initial receive flow-control window size in bytes.</summary>
    internal int InitialWindowSize { get; }

    /// <summary>The configured maximum batch weight in bytes for HTTP/2 frame encoding.</summary>
    internal long MaxBatchWeight { get; }

    public BidiFlow<HttpRequestMessage, IOutputItem, IInputItem, HttpResponseMessage, NotUsed> CreateFlow()
    {
        return BidiFlow.FromGraph(GraphDsl.Create(b =>
        {
            var frameEncoder = b.Add(new Http20EncoderStage(InitialWindowSize));
            var frameDecoder = b.Add(new Http20DecoderStage());
            var connection = b.Add(new Http20ConnectionStage(InitialWindowSize, MaxConcurrentStreams));
            var signalMerge = b.Add(new MergePreferred<IOutputItem>(1));

            // Accumulate frames from OutServer into batches before encoding.
            // Each batch is serialised into a single NetworkBuffer, eliminating N-1
            // intermediate allocations and memory copies under concurrent load.
            var frameBatch = b.Add(
                Flow.Create<Http2Frame>()
                    .BatchWeighted(
                        MaxBatchWeight,
                        f => (long)f.SerializedSize,
                        f => new List<Http2Frame>(4) { f },
                        (list, f) => { list.Add(f); return list; }));


            b.From(connection.OutServer).To(frameBatch.Inlet);
            b.From(frameBatch.Outlet).To(frameEncoder.Inlet);
            b.From(frameDecoder.Outlet).To(connection.InServer);

            var signalCast = b.Add(_signalCast);

            // Encoder emits RFC 9113 §3.4 preface on its first pull (before any frame is encoded).
            // PrependPrefaceStage stage removed — preface is now inline in Http20EncoderStage.
            b.From(frameEncoder.Outlet).To(signalMerge.In(0));
            b.From(connection.OutSignal).Via(signalCast).To(signalMerge.Preferred);

            return new BidiShape<
                HttpRequestMessage,
                IOutputItem,
                IInputItem,
                HttpResponseMessage>(
                connection.InApp,
                signalMerge.Out,
                frameDecoder.Inlet,
                connection.OutResponse);
        }));
    }

    internal static IOutputItem BatchConsolidate(IOutputItem accumulated, IOutputItem next)
    {
        if (accumulated is not NetworkBuffer acc || next is not NetworkBuffer nxt) return next;
        var totalLength = acc.Length + nxt.Length;

        // Fast path: if the accumulated buffer has enough capacity, append in-place (zero-alloc).
        // MemoryPool.Rent often returns buffers larger than requested, so this is common.
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