using Akka.Streams;
using Akka.Streams.Stage;
using Servus.Akka.Transport;
using TurboHTTP.Protocol.Syntax.Http3.Server;

namespace TurboHTTP.Streams.Stages.Server;

internal sealed class Http30ConnectionStage : GraphStage<ConnectionShape>
{
    private readonly Inlet<ITransportInbound> _inNetwork = new("Http30Connection.In.Network");
    private readonly Outlet<HttpRequestMessage> _outRequest = new("Http30Connection.Out.Request");
    private readonly Inlet<HttpResponseMessage> _inResponse = new("Http30Connection.In.Response");
    private readonly Outlet<ITransportOutbound> _outNetwork = new("Http30Connection.Out.Network");

    private readonly long _maxRequestBodySize;
    private readonly TimeSpan _keepAliveTimeout;
    private readonly TimeSpan _requestHeadersTimeout;
    private readonly int _minBodyDataRate;
    private readonly TimeSpan _bodyRateGracePeriod;

    public Http30ConnectionStage(
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

    public override ConnectionShape Shape => new(_inNetwork, _outRequest, _inResponse, _outNetwork);

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new HttpConnectionServerStageLogic<Http3ServerStateMachine>(this,
            ops => new Http3ServerStateMachine(
                ops,
                _maxRequestBodySize,
                _keepAliveTimeout,
                _requestHeadersTimeout,
                _minBodyDataRate,
                _bodyRateGracePeriod));
}

