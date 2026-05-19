using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using Servus.Akka.Transport;
using TurboHTTP.Streams.Stages.Server;

namespace TurboHTTP.Streams;

internal sealed class Http30ServerEngine : IServerProtocolEngine
{
    private readonly long _maxRequestBodySize;
    private readonly TimeSpan _keepAliveTimeout;
    private readonly TimeSpan _requestHeadersTimeout;
    private readonly int _minBodyDataRate;
    private readonly TimeSpan _bodyRateGracePeriod;

    public Http30ServerEngine(
        long maxRequestBodySize = 30 * 1024 * 1024,
        TimeSpan? keepAliveTimeout = null,
        TimeSpan? requestHeadersTimeout = null,
        int minBodyDataRate = 240,
        TimeSpan? bodyRateGracePeriod = null)
    {
        _maxRequestBodySize = maxRequestBodySize;
        _keepAliveTimeout = keepAliveTimeout ?? TimeSpan.FromSeconds(130);
        _requestHeadersTimeout = requestHeadersTimeout ?? TimeSpan.FromSeconds(30);
        _minBodyDataRate = minBodyDataRate;
        _bodyRateGracePeriod = bodyRateGracePeriod ?? TimeSpan.FromSeconds(5);
    }

    public BidiFlow<ITransportInbound, HttpRequestMessage, HttpResponseMessage, ITransportOutbound, NotUsed> CreateFlow()
    {
        return BidiFlow.FromGraph(GraphDsl.Create(b =>
        {
            var connection = b.Add(new Http30ConnectionStage(
                _maxRequestBodySize,
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