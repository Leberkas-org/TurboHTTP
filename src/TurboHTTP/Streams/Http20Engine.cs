using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHTTP.Internal;
using TurboHTTP.Protocol.Http2;
using TurboHTTP.Streams.Stages;
using TurboHTTP.Streams.Stages.Decoding;

namespace TurboHTTP.Streams;

public class Http20Engine : IHttpProtocolEngine
{
    public Http20Engine() : this(1_048_576, int.MaxValue)
    {
    }

    public Http20Engine(int initialWindowSize, int maxConcurrentStreams)
    {
        InitialWindowSize = initialWindowSize;
        MaxConcurrentStreams = maxConcurrentStreams;
    }

    /// <summary>
    /// The configured initial MAX_CONCURRENT_STREAMS limit.
    /// This value is passed to the underlying <see cref="Http20ConnectionStage"/>
    /// and will be updated at runtime when the server sends a SETTINGS frame
    /// with <see cref="Protocol.Http2.SettingsParameter.MaxConcurrentStreams"/>.
    /// </summary>
    public int MaxConcurrentStreams { get; }

    /// <summary>The configured initial receive flow-control window size in bytes.</summary>
    internal int InitialWindowSize { get; }

    public BidiFlow<HttpRequestMessage, IOutputItem, IInputItem, HttpResponseMessage, NotUsed> CreateFlow()
    {
        return BidiFlow.FromGraph(GraphDsl.Create(b =>
        {
            var connection = b.Add(new Http20ConnectionStage(
                new Http2ConnectionConfig(InitialWindowSize, MaxConcurrentStreams)));

            return new BidiShape<
                HttpRequestMessage,
                IOutputItem,
                IInputItem,
                HttpResponseMessage>(
                connection.InApp,
                connection.OutNetwork,
                connection.InServer,
                connection.OutResponse);
        }));
    }
}