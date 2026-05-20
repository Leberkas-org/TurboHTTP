using Akka.Streams;
using Akka.Streams.Stage;
using Servus.Akka.Transport;
using TurboHTTP.Protocol.Syntax.Http2.Server;

namespace TurboHTTP.Streams.Stages.Server;

internal sealed class Http20ConnectionStage : GraphStage<ConnectionShape>
{
    private readonly Inlet<ITransportInbound> _inNetwork = new("Http20Connection.In.Network");
    private readonly Outlet<HttpRequestMessage> _outRequest = new("Http20Connection.Out.Request");
    private readonly Inlet<HttpResponseMessage> _inResponse = new("Http20Connection.In.Response");
    private readonly Outlet<ITransportOutbound> _outNetwork = new("Http20Connection.Out.Network");

    private readonly int _maxConcurrentStreams;
    private readonly int _initialConnectionWindowSize;
    private readonly int _initialStreamWindowSize;
    private readonly int _maxFrameSize;
    private readonly TimeSpan _keepAliveTimeout;
    private readonly TimeSpan _requestHeadersTimeout;
    private readonly int _minBodyDataRate;
    private readonly TimeSpan _bodyRateGracePeriod;

    public Http20ConnectionStage(
        int maxConcurrentStreams = 100,
        int initialConnectionWindowSize = 65535,
        int initialStreamWindowSize = 65535,
        int maxFrameSize = 16384,
        TimeSpan? keepAliveTimeout = null,
        TimeSpan? requestHeadersTimeout = null,
        int minBodyDataRate = 240,
        TimeSpan? bodyRateGracePeriod = null)
    {
        _maxConcurrentStreams = maxConcurrentStreams;
        _initialConnectionWindowSize = initialConnectionWindowSize;
        _initialStreamWindowSize = initialStreamWindowSize;
        _maxFrameSize = maxFrameSize;
        _keepAliveTimeout = keepAliveTimeout ?? TimeSpan.FromSeconds(130);
        _requestHeadersTimeout = requestHeadersTimeout ?? TimeSpan.FromSeconds(30);
        _minBodyDataRate = minBodyDataRate;
        _bodyRateGracePeriod = bodyRateGracePeriod ?? TimeSpan.FromSeconds(5);
    }

    public override ConnectionShape Shape => new(_inNetwork, _outRequest, _inResponse, _outNetwork);

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new HttpConnectionServerStageLogic<Http2ServerStateMachine>(this,
            ops => new Http2ServerStateMachine(
                ops,
                _maxConcurrentStreams,
                _initialConnectionWindowSize,
                _initialStreamWindowSize,
                keepAliveTimeout: _keepAliveTimeout,
                requestHeadersTimeout: _requestHeadersTimeout,
                minRequestBodyDataRate: _minBodyDataRate,
                minRequestBodyDataRateGracePeriod: _bodyRateGracePeriod));
}
