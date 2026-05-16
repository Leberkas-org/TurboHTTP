using Servus.Akka.Transport;
using TurboHTTP.Internal;
using TurboHTTP.Protocol.Multiplexed;
using TurboHTTP.Streams.Stages;
using static Servus.Core.Servus;

namespace TurboHTTP.Protocol.Syntax.Http2.Client;

internal sealed class Http2ClientStateMachine : IClientStateMachine
{
    private readonly Http2ClientSessionManager _clientSession;
    private readonly ReconnectionManager _reconnect;
    private readonly IStageOperations _ops;
    private readonly TurboClientOptions _options;
    private TransportOptions? _transportOptions;

    private const string KeepAlivePingTimerKey = "keep-alive-ping";
    private const string KeepAlivePingTimeoutKey = "keep-alive-ping-timeout";

    private bool KeepAliveEnabled => _options.Http2.KeepAlivePingDelay != Timeout.InfiniteTimeSpan;

    public bool CanAcceptRequest =>
        !_clientSession.GoAwayReceived && !_reconnect.IsReconnecting && _clientSession.CanOpenStream;

    public bool HasInFlightRequests => _clientSession.HasInFlightRequests;
    public bool IsReconnecting => _reconnect.IsReconnecting;
    public RequestEndpoint Endpoint => _clientSession.Endpoint;
    public int ReconnectBufferCount => _reconnect.BufferedCount;

    public Http2ClientStateMachine(TurboClientOptions options, IStageOperations ops)
    {
        _options = options;
        _ops = ops;
        _clientSession = new Http2ClientSessionManager(options, ops);
        _reconnect = new ReconnectionManager(options.Http2.MaxReconnectAttempts);
    }

    public void PreStart()
    {
        var preface = _clientSession.TryBuildPreface();
        if (preface is not null)
        {
            _ops.OnOutbound(preface);
        }
    }

    public void OnRequest(HttpRequestMessage request)
    {
        _clientSession.EncodeRequest(request);
    }

    public void DecodeServerData(ITransportInbound data)
    {
        switch (data)
        {
            case TransportConnected:
                OnConnectionRestored();
                return;

            case TransportDisconnected when _reconnect.IsReconnecting:
                OnReconnectAttemptFailed();
                return;

            case TransportDisconnected when _clientSession.HasInFlightRequests:
                OnConnectionLost(lastStreamId: 0);
                return;

            case TransportDisconnected:
                return;
        }

        if (data is not TransportData { Buffer: var buffer })
        {
            return;
        }

        var frames = _clientSession.DecodeFrames(buffer);
        for (var i = 0; i < frames.Count; i++)
        {
            _clientSession.ProcessFrame(frames[i]);
        }

        if (_clientSession is { GoAwayReceived: true, HasInFlightRequests: true })
        {
            OnConnectionLost(_clientSession.GoAwayLastStreamId);
            return;
        }

        if (frames.Count > 0)
        {
            ResetKeepAliveTimer();
        }
    }

    public void OnUpstreamFinished()
    {
        if (_reconnect.IsReconnecting)
        {
            _reconnect.FailAllBuffered(new HttpRequestException("HTTP/2 transport closed during reconnect."));
            _reconnect.Reset();
            Tracing.For("Protocol").Debug(this, "HTTP/2 transport closed during reconnect");
        }
    }

    public void OnTimerFired(string name)
    {
        switch (name)
        {
            case KeepAlivePingTimerKey:
                {
                    var policy = _options.Http2.KeepAlivePingPolicy;
                    if (policy == HttpKeepAlivePingPolicy.WithActiveRequests && !_clientSession.HasInFlightRequests)
                    {
                        return;
                    }

                    _clientSession.SendKeepAlivePing();
                    ScheduleKeepAlivePingTimeout();
                    break;
                }
            case KeepAlivePingTimeoutKey:
                {
                    if (_clientSession.IsKeepAliveTimedOut(_options.Http2.KeepAlivePingTimeout))
                    {
                        Tracing.For("Protocol").Info(this, "HTTP/2: Keep-alive PING timeout — closing connection");
                        if (_clientSession.HasInFlightRequests)
                        {
                            OnConnectionLost(lastStreamId: 0);
                        }
                    }

                    break;
                }
        }
    }

    public void OnBodyMessage(object msg) => _clientSession.OnBodyMessage(msg);

    public void Cleanup() => _clientSession.Cleanup();

    private void OnConnectionLost(int lastStreamId)
    {
        var replayable = ClassifyStreamsForReplay(lastStreamId);
        _reconnect.OnConnectionLost(replayable);

        _clientSession.ReleaseAllStreamState();
        _clientSession.ResetConnectionState();

        _transportOptions ??= OptionsFactory.Build(_clientSession.Endpoint, _options);
        _ops.OnOutbound(new ConnectTransport(_transportOptions));
    }

    private List<HttpRequestMessage> ClassifyStreamsForReplay(int lastStreamId)
    {
        var replayable = new List<HttpRequestMessage>();

        foreach (var (streamId, request) in _clientSession.GetCorrelationMap())
        {
            if (IsStreamSafeToReplay(streamId, request, lastStreamId))
            {
                replayable.Add(request);
            }
            else
            {
                Tracing.For("Protocol").Info(this,
                    "HTTP/2: Dropping non-idempotent or partially-responded request {0} {1} on reconnect",
                    request.Method, request.RequestUri);
                request.Fail(
                    new HttpRequestException("Non-idempotent or partially-responded request dropped on reconnect."));
                request.Dispose();
            }
        }

        return replayable;
    }

    private bool IsStreamSafeToReplay(int streamId, HttpRequestMessage request, int lastStreamId)
    {
        if (lastStreamId > 0 && streamId > lastStreamId)
        {
            return true;
        }

        return IsIdempotentMethod(request.Method) && !_clientSession.HasReceivedHeaders(streamId);
    }

    private static bool IsIdempotentMethod(HttpMethod method)
        => method == HttpMethod.Get
           || method == HttpMethod.Head
           || method == HttpMethod.Options
           || method == HttpMethod.Trace
           || method == HttpMethod.Delete
           || method == HttpMethod.Put;

    private void OnConnectionRestored()
    {
        var preface = _clientSession.TryBuildPreface();
        if (preface is not null)
        {
            _ops.OnOutbound(preface);
        }

        var toReplay = _reconnect.OnConnectionRestored();
        for (var i = 0; i < toReplay.Count; i++)
        {
            _clientSession.EncodeRequest(toReplay[i]);
        }

        ScheduleKeepAlivePing();
    }

    private void OnReconnectAttemptFailed()
    {
        if (!_reconnect.OnReconnectAttemptFailed())
        {
            Tracing.For("Protocol").Info(this, "HTTP/2 reconnect failed after max attempts");
            _reconnect.FailAllBuffered(new HttpRequestException("HTTP/2 reconnect failed after max attempts."));
            return;
        }

        _ops.OnOutbound(new ConnectTransport(_transportOptions!));
    }

    private void ScheduleKeepAlivePing()
    {
        if (KeepAliveEnabled)
        {
            _ops.OnScheduleTimer(KeepAlivePingTimerKey, _options.Http2.KeepAlivePingDelay);
        }
    }

    private void ScheduleKeepAlivePingTimeout()
    {
        if (KeepAliveEnabled)
        {
            _ops.OnScheduleTimer(KeepAlivePingTimeoutKey, _options.Http2.KeepAlivePingTimeout);
        }
    }

    private void ResetKeepAliveTimer()
    {
        if (KeepAliveEnabled)
        {
            _ops.OnCancelTimer(KeepAlivePingTimeoutKey);
            ScheduleKeepAlivePing();
        }
    }
}