using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using TurboHTTP.Protocol.Syntax.Http3.Options;
using TurboHTTP.Server;
using TurboHTTP.Streams.Stages.Server;

namespace TurboHTTP.Protocol.Syntax.Http3.Server;

internal sealed class Http3ServerStateMachine : IServerStateMachine
{
    private const string DrainBodyPrefix = "drain-body:";
    private const string HeadersTimeoutPrefix = "headers-timeout:";
    private const string KeepAliveTimeout = "keep-alive-timeout";
    private const string BodyRateCheck = "body-rate-check";
    private const string BodyConsumptionPrefix = "body-consumption:";

    private readonly IServerStageOperations _ops;
    private readonly Http3ServerSessionManager _sessionManager;

    private readonly TimeSpan _keepAliveTimeout;
    private readonly TimeSpan _requestHeadersTimeout;
    private readonly int _minBodyDataRate;
    private readonly TimeSpan _bodyRateGracePeriod;
    private int _activeStreamCount;

    public bool CanAcceptResponse => _sessionManager.ActiveStreamCount > 0;
    public bool ShouldComplete => false;
    public int MaxQueuedRequests => _sessionManager.MaxConcurrentStreams;

    public Http3ServerStateMachine(TurboServerOptions options, IServerStageOperations ops)
    {
        _ops = ops ?? throw new ArgumentNullException(nameof(ops));
        ArgumentNullException.ThrowIfNull(options);

        var shared = SharedHttpOptions.Default with
        {
            MaxBufferedBodySize = options.BodyBufferThreshold,
            MaxStreamedBodySize = options.Http3.MaxRequestBodySize,
            MaxHeaderBytes = options.Http3.MaxHeaderListSize,
        };

        var encoderOpts = new Http3ServerEncoderOptions
        {
            Shared = shared,
            QpackMaxTableCapacity = options.Http3.QpackMaxTableCapacity,
        };

        var decoderOpts = new Http3ServerDecoderOptions
        {
            Shared = shared,
            MaxConcurrentStreams = options.Http3.MaxConcurrentStreams,
            MaxFieldSectionSize = options.Http3.MaxHeaderListSize,
        };

        _sessionManager = new Http3ServerSessionManager(encoderOpts, decoderOpts, ops, options.Http3.MaxRequestBodySize,
            options.ResponseBodyChunkSize, options.BodyConsumptionTimeout);

        _keepAliveTimeout = options.Http3.KeepAliveTimeout;
        _requestHeadersTimeout = options.Http3.RequestHeadersTimeout;
        _minBodyDataRate = options.Http3.MinRequestBodyDataRate;
        _bodyRateGracePeriod = options.Http3.MinRequestBodyDataRateGracePeriod;
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

    public void OnResponse(IFeatureCollection features)
    {
        _sessionManager.OnResponse(features);
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
            return;
        }

        if (name.StartsWith(BodyConsumptionPrefix))
        {
            if (long.TryParse(name.AsSpan(BodyConsumptionPrefix.Length), out var consumptionStreamId))
            {
                _sessionManager.EmitRstStream(consumptionStreamId, ErrorCode.GeneralProtocolError);
            }
        }
    }

    public void OnBodyMessage(object msg)
    {
        _sessionManager.OnBodyMessage(msg);
    }

    public void Cleanup() => _sessionManager.Cleanup();
}
