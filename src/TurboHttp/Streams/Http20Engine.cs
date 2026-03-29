using System.Buffers;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.Internal;
using TurboHttp.Protocol.RFC9113;
using TurboHttp.Streams.Stages.Decoding;
using TurboHttp.Streams.Stages.Encoding;
using TurboHttp.Streams.Stages.Routing;

namespace TurboHttp.Streams;

public class Http20Engine : IHttpProtocolEngine
{
    internal const long MaxBatchWeight = 65_536;

    /// <summary>
    /// Default initial value for MAX_CONCURRENT_STREAMS before the server sends its SETTINGS.
    /// RFC 9113 §6.5.2: "Initially, there is no limit to this value."
    /// We use <see cref="int.MaxValue"/> to represent "unlimited".
    /// </summary>
    internal const int DefaultMaxConcurrentStreams = int.MaxValue;

    private readonly int _initialWindowSize;
    private readonly int _maxConcurrentStreams;

    public Http20Engine() : this(65535)
    {
    }

    public Http20Engine(int initialWindowSize, int maxConcurrentStreams = DefaultMaxConcurrentStreams)
    {
        _initialWindowSize = initialWindowSize;
        _maxConcurrentStreams = maxConcurrentStreams;
    }

    /// <summary>
    /// The configured initial MAX_CONCURRENT_STREAMS limit.
    /// This value is passed to the underlying <see cref="Http20ConnectionStage"/>
    /// and will be updated at runtime when the server sends a SETTINGS frame
    /// with <see cref="SettingsParameter.MaxConcurrentStreams"/>.
    /// </summary>
    public int MaxConcurrentStreams => _maxConcurrentStreams;

    public BidiFlow<HttpRequestMessage, IOutputItem, IInputItem, HttpResponseMessage, NotUsed> CreateFlow()
    {
        var requestEncoder = new Http2RequestEncoder();
        var windowSize = _initialWindowSize;

        return BidiFlow.FromGraph(GraphDsl.Create(b =>
        {
            ;
            var streamIdAllocator = b.Add(new Http20StreamIdAllocatorStage());
            var broadcast = b.Add(new Broadcast<(HttpRequestMessage, int)>(2));
            var requestToFrame = b.Add(new Http20Request2FrameStage(requestEncoder));
            var frameEncoder = b.Add(new Http20EncoderStage());
            var frameDecoder = b.Add(new Http20DecoderStage());
            var streamDecoder = b.Add(new Http20StreamStage());
            var correlation = b.Add(new Http20CorrelationStage());
            var prependPreface = b.Add(new Http20PrependPrefaceStage());
            var connection = b.Add(new Http20ConnectionStage(windowSize, _maxConcurrentStreams));
            var signalMerge = b.Add(new MergePreferred<IOutputItem>(1));

            // Request path: allocate stream ID → broadcast to both frame encoder and correlation
            b.From(streamIdAllocator.Outlet).To(broadcast.In);
            b.From(broadcast.Out(0)).To(requestToFrame.Inlet);
            b.From(broadcast.Out(1)).To(correlation.In0);

            b.From(requestToFrame.Outlet).To(connection.InApp);
            b.From(connection.OutServer).To(frameEncoder.Inlet);
            b.From(frameDecoder.Outlet).To(connection.InServer);

            // Response path: frames → stream decoder → correlation (sets RequestMessage)
            b.From(connection.OutStream).To(streamDecoder.Inlet);
            b.From(streamDecoder.Outlet).To(correlation.In1);

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
                correlation.Out);
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