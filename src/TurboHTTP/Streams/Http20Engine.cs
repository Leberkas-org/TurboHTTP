using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHTTP.Internal;
using TurboHTTP.Streams.Stages;
using TurboHTTP.Streams.Stages.Internal;

namespace TurboHTTP.Streams;

internal record Http2EngineOptions(
    int MaxConnectionsPerServer,
    int InitialConcurrentStreams,
    int InitialConnectionWindowSize,
    int InitialStreamWindowSize,
    int MaxFrameSize,
    int HeaderTableSize,
    int MaxReconnectAttempts,
    int MaxBatchWeight,
    TimeSpan KeepAlivePingDelay,
    TimeSpan KeepAlivePingTimeout,
    HttpKeepAlivePingPolicy KeepAlivePingPolicy);

internal class Http20Engine : IHttpProtocolEngine
{
    private readonly Http2EngineOptions _options;

    public Http20Engine(Http2EngineOptions options)
    {
        _options = options;
    }

    public BidiFlow<HttpRequestMessage, IOutputItem, IInputItem, HttpResponseMessage, NotUsed> CreateFlow()
    {
        return BidiFlow.FromGraph(GraphDsl.Create(b =>
        {
            var connection = b.Add(new Http20ConnectionStage(_options));

            // Coalesce consecutive NetworkBuffer frames (HEADERS, DATA, WINDOW_UPDATE, …)
            // from the H2 connection stage into fewer, larger writes — reducing socket
            // syscall count under concurrent multiplexed streams.  Control items are
            // flushed through immediately so H2 frame ordering is preserved.
            var batchFlow = b.Add(new NetworkBufferBatchStage(_options.MaxBatchWeight));

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
}