using Servus.Akka.Transport;
using TurboHTTP.Protocol.Syntax.Http2.Options;
using TurboHTTP.Streams;
using TurboHTTP.Streams.Stages.Server;

namespace TurboHTTP.Protocol.Syntax.Http2.Server;

internal sealed class Http2ServerStateMachine : IServerStateMachine
{
    private const string DrainBodyPrefix = "drain-body:";
    private const string HeadersTimeoutPrefix = "headers-timeout:";
    private const string KeepAliveTimeout = "keep-alive-timeout";
    private const string BodyRateCheck = "body-rate-check:";

    private readonly IServerStageOperations _ops;
    private readonly Http2ServerSessionManager _sessionManager;

    private readonly TimeSpan _keepAliveTimeout;
    private readonly TimeSpan _requestHeadersTimeout;
    private readonly int _minBodyDataRate;
    private readonly TimeSpan _bodyRateGracePeriod;
    private int _activeStreamCount;

    public bool CanAcceptResponse => _sessionManager.ActiveStreamCount > 0;
    public bool ShouldComplete => false;

    public Http2ServerStateMachine(
        IServerStageOperations ops,
        int maxConcurrentStreams = 100,
        int initialConnectionWindowSize = 65535,
        int initialStreamWindowSize = 65535,
        int maxHeaderSize = 16 * 1024,
        int maxTotalHeaderSize = 64 * 1024,
        long maxRequestBodySize = 30 * 1024 * 1024,
        TimeSpan? keepAliveTimeout = null,
        TimeSpan? requestHeadersTimeout = null,
        int minRequestBodyDataRate = 240,
        TimeSpan? minRequestBodyDataRateGracePeriod = null)
    {
        _ops = ops ?? throw new ArgumentNullException(nameof(ops));

        var encoderOpts = new Http2ServerEncoderOptions();

        var decoderOpts = new Http2ServerDecoderOptions
        {
            MaxConcurrentStreams = maxConcurrentStreams,
        };

        _sessionManager = new Http2ServerSessionManager(
            encoderOpts,
            decoderOpts,
            ops,
            initialConnectionWindowSize,
            initialStreamWindowSize,
            maxRequestBodySize);

        _keepAliveTimeout = keepAliveTimeout ?? TimeSpan.FromSeconds(130);
        _requestHeadersTimeout = requestHeadersTimeout ?? TimeSpan.FromSeconds(30);
        _minBodyDataRate = minRequestBodyDataRate;
        _bodyRateGracePeriod = minRequestBodyDataRateGracePeriod ?? TimeSpan.FromSeconds(5);
    }

    public void PreStart()
    {
        _sessionManager.PreStart();
        _ops.OnScheduleTimer("keep-alive-timeout", _keepAliveTimeout);
    }

    public void DecodeClientData(ITransportInbound data)
    {
        if (data is not TransportData { Buffer: var buffer })
        {
            return;
        }

        _sessionManager.DecodeClientData(buffer);

        var streamCount = _sessionManager.ActiveStreamCount;
        switch (streamCount)
        {
            case > 0 when _activeStreamCount == 0:
                _activeStreamCount = streamCount;
                _ops.OnCancelTimer(KeepAliveTimeout);
                break;
            case 0 when _activeStreamCount > 0:
                _activeStreamCount = 0;
                _ops.OnScheduleTimer(KeepAliveTimeout, _keepAliveTimeout);
                break;
            default:
                _activeStreamCount = streamCount;
                break;
        }
    }

    public void OnResponse(HttpResponseMessage response) => _sessionManager.OnResponse(response);

    public void OnDownstreamFinished()
    {
    }

    public void OnTimerFired(string name)
    {
        if (name == KeepAliveTimeout)
        {
            _sessionManager.EmitGoAway(0, Http2ErrorCode.NoError, "Keep-alive timeout");
            return;
        }

        if (name.StartsWith(DrainBodyPrefix))
        {
            if (int.TryParse(name.AsSpan(DrainBodyPrefix.Length), out var drainStreamId))
            {
                _sessionManager.DrainOutboundBuffer(drainStreamId);
            }

            return;
        }

        if (name.StartsWith(HeadersTimeoutPrefix))
        {
            if (int.TryParse(name.AsSpan(HeadersTimeoutPrefix.Length), out var streamId))
            {
                _sessionManager.EmitRstStream(streamId, Http2ErrorCode.EnhanceYourCalm);
            }

            return;
        }

        if (name == BodyRateCheck)
        {
            _sessionManager.CheckBodyRates(_minBodyDataRate, _bodyRateGracePeriod);
        }
    }

    public void OnBodyMessage(object msg) => _sessionManager.OnBodyMessage(msg);

    public void Cleanup() => _sessionManager.Cleanup();
}