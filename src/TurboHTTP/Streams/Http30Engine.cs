using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHTTP.Internal;
using TurboHTTP.Streams.Stages;
using TurboHTTP.Streams.Stages.Internal;

namespace TurboHTTP.Streams;

internal sealed record Http3EngineOptions(
    int MaxFieldSectionSize,
    int QpackMaxTableCapacity,
    int QpackBlockedStreams,
    TimeSpan IdleTimeout,
    int MaxReconnectAttempts,
    bool AllowServerPush,
    bool AllowEarlyData,
    bool AllowConnectionMigration);

internal sealed class Http30Engine : IHttpProtocolEngine
{
    private readonly Http3EngineOptions _options;

    public Http30Engine(Http3EngineOptions options)
    {
        _options = options;
    }

    public BidiFlow<HttpRequestMessage, IOutputItem, IInputItem, HttpResponseMessage, NotUsed> CreateFlow()
    {
        return BidiFlow.FromGraph(GraphDsl.Create(b =>
        {
            var connection = b.Add(new Http30ConnectionStage(_options));

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