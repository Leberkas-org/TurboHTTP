using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using Servus.Akka.Transport;
using TurboHTTP.Streams.Stages.Server;

namespace TurboHTTP.Streams;

internal sealed class Http20ServerEngine : IServerProtocolEngine
{
    private readonly int _maxConcurrentStreams;
    private readonly int _initialWindowSize;
    private readonly int _maxFrameSize;
    private readonly TimeSpan _keepAliveTimeout;
    private readonly TimeSpan _requestHeadersTimeout;
    private readonly int _minBodyDataRate;
    private readonly TimeSpan _bodyRateGracePeriod;

    public Http20ServerEngine(
        int maxConcurrentStreams = 100,
        int initialWindowSize = 65535,
        int maxFrameSize = 16384,
        TimeSpan? keepAliveTimeout = null,
        TimeSpan? requestHeadersTimeout = null,
        int minBodyDataRate = 240,
        TimeSpan? bodyRateGracePeriod = null)
    {
        _maxConcurrentStreams = maxConcurrentStreams;
        _initialWindowSize = initialWindowSize;
        _maxFrameSize = maxFrameSize;
        _keepAliveTimeout = keepAliveTimeout ?? TimeSpan.FromSeconds(130);
        _requestHeadersTimeout = requestHeadersTimeout ?? TimeSpan.FromSeconds(30);
        _minBodyDataRate = minBodyDataRate;
        _bodyRateGracePeriod = bodyRateGracePeriod ?? TimeSpan.FromSeconds(5);
    }

    public BidiFlow<ITransportInbound, HttpRequestMessage, HttpResponseMessage, ITransportOutbound, NotUsed> CreateFlow()
    {
        return BidiFlow.FromGraph(GraphDsl.Create(b =>
        {
            var connection = b.Add(new Http20ConnectionStage(
                _maxConcurrentStreams,
                _initialWindowSize,
                _maxFrameSize,
                _keepAliveTimeout,
                _requestHeadersTimeout,
                _minBodyDataRate,
                _bodyRateGracePeriod));

            return new BidiShape<
                ITransportInbound,
                HttpRequestMessage,
                HttpResponseMessage,
                ITransportOutbound>(
                connection.InNetwork,
                connection.OutRequest,
                connection.InResponse,
                connection.OutNetwork);
        }));
    }
}
