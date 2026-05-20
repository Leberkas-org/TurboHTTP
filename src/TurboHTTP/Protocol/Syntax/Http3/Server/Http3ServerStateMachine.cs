using Servus.Akka.Transport;
using TurboHTTP.Protocol.Syntax.Http3.Options;
using TurboHTTP.Streams;
using TurboHTTP.Streams.Stages.Server;

namespace TurboHTTP.Protocol.Syntax.Http3.Server;

internal sealed class Http3ServerStateMachine : IServerStateMachine
{
    private const string DrainBodyPrefix = "drain-body:";
    private const string HeadersTimeoutPrefix = "headers-timeout:";
    private const string KeepAliveTimeout = "keep-alive-timeout";
    private const string BodyRateCheck = "body-rate-check";

    private readonly IServerStageOperations _ops;
    private readonly Http3ServerSessionManager _sessionManager;

    private readonly TimeSpan _keepAliveTimeout;
    private readonly TimeSpan _requestHeadersTimeout;
    private readonly int _minBodyDataRate;
    private readonly TimeSpan _bodyRateGracePeriod;
    private int _activeStreamCount;

    public bool CanAcceptResponse => _sessionManager.ActiveStreamCount > 0;
    public bool ShouldComplete => false;

    public Http3ServerStateMachine(
        IServerStageOperations ops,
        long maxRequestBodySize = 30 * 1024 * 1024,
        TimeSpan? keepAliveTimeout = null,
        TimeSpan? requestHeadersTimeout = null,
        int minBodyDataRate = 240,
        TimeSpan? bodyRateGracePeriod = null)
    {
        _ops = ops ?? throw new ArgumentNullException(nameof(ops));

        var encoderOpts = new Http3ServerEncoderOptions
        {
            QpackMaxTableCapacity = 4096,
        };

        var decoderOpts = new Http3ServerDecoderOptions
        {
            MaxConcurrentStreams = 100,
            MaxFieldSectionSize = 64 * 1024,
        };

        _sessionManager = new Http3ServerSessionManager(encoderOpts, decoderOpts, ops, maxRequestBodySize);

        _keepAliveTimeout = keepAliveTimeout ?? TimeSpan.FromSeconds(130);
        _requestHeadersTimeout = requestHeadersTimeout ?? TimeSpan.FromSeconds(30);
        _minBodyDataRate = minBodyDataRate;
        _bodyRateGracePeriod = bodyRateGracePeriod ?? TimeSpan.FromSeconds(5);
    }

    public void PreStart()
    {
        _sessionManager.PreStart();
        _ops.OnScheduleTimer(KeepAliveTimeout, _keepAliveTimeout);
    }

    public void DecodeClientData(ITransportInbound data)
    {
        _sessionManager.DecodeClientData(data);

        var streamCount = _sessionManager.ActiveStreamCount;
        if (streamCount > 0 && _activeStreamCount == 0)
        {
            _activeStreamCount = streamCount;
            _ops.OnCancelTimer(KeepAliveTimeout);
        }
        else if (streamCount == 0 && _activeStreamCount > 0)
        {
            _activeStreamCount = 0;
            _ops.OnScheduleTimer(KeepAliveTimeout, _keepAliveTimeout);
        }
        else
        {
            _activeStreamCount = streamCount;
        }
    }

    public void OnResponse(HttpResponseMessage response)
    {
        _sessionManager.OnResponse(response);
    }

    public void OnDownstreamFinished()
    {
        _sessionManager.FlushAllPendingRequests();
    }

    public void OnTimerFired(string name)
    {
        if (name == KeepAliveTimeout)
        {
            if (_activeStreamCount == 0)
            {
                return;
            }

            _ops.OnScheduleTimer(KeepAliveTimeout, _keepAliveTimeout);
            return;
        }

        if (name.StartsWith(DrainBodyPrefix))
        {
            if (long.TryParse(name.AsSpan(DrainBodyPrefix.Length), out var drainStreamId))
            {
                _sessionManager.DrainOutboundBuffer(drainStreamId);
            }

            return;
        }

        if (name.StartsWith(HeadersTimeoutPrefix))
        {
            if (long.TryParse(name.AsSpan(HeadersTimeoutPrefix.Length), out var streamId))
            {
                _sessionManager.EmitRstStream(streamId, ErrorCode.GeneralProtocolError);
            }

            return;
        }

        if (name == BodyRateCheck)
        {
            _sessionManager.CheckBodyRates(_minBodyDataRate, _bodyRateGracePeriod);
        }
    }

    public void OnBodyMessage(object msg)
    {
        _sessionManager.OnBodyMessage(msg);
    }

    public void Cleanup() => _sessionManager.Cleanup();
}
