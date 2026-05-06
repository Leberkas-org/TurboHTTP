using System.Buffers;
using Servus.Akka.Transport;
using TurboHTTP.Internal;
using TurboHTTP.Streams.Stages;

namespace TurboHTTP.Protocol.Http2;

internal sealed class StateMachine : IHttpStateMachine
{
    private readonly ProtocolHandler _protocol;
    private readonly IStageOperations _ops;
    private readonly TurboClientOptions _options;
    private readonly List<HttpRequestMessage> _reconnectBuffer = [];
    private int _reconnectAttempts;
    private TransportOptions? _transportOptions;

    private const string KeepAlivePingTimerKey = "keep-alive-ping";
    private const string KeepAlivePingTimeoutKey = "keep-alive-ping-timeout";

    private bool KeepAliveEnabled => _options.Http2.KeepAlivePingDelay != Timeout.InfiniteTimeSpan;

    public bool CanAcceptRequest => !_protocol.GoAwayReceived && !IsReconnecting && _protocol.CanOpenStream;
    public bool HasInFlightRequests => _protocol.HasInFlightRequests;
    public bool IsReconnecting { get; private set; }
    public RequestEndpoint Endpoint => _protocol.Endpoint;
    public int ReconnectBufferCount => _reconnectBuffer.Count;

    public StateMachine(TurboClientOptions options, IStageOperations ops)
    {
        _options = options;
        _ops = ops;
        _protocol = new ProtocolHandler(options, ops);
    }

    public void PreStart()
    {
        var preface = _protocol.TryBuildPreface();
        if (preface is not null)
        {
            _ops.OnOutbound(preface);
        }
    }

    public void OnRequest(HttpRequestMessage request)
    {
        _protocol.EncodeRequest(request);
    }

    public void DecodeServerData(ITransportInbound data)
    {
        switch (data)
        {
            case TransportConnected:
                OnConnectionRestored();
                return;

            case TransportDisconnected when IsReconnecting:
                OnReconnectAttemptFailed();
                return;

            case TransportDisconnected when _protocol.HasInFlightRequests:
                OnConnectionLost(lastStreamId: 0);
                return;

            case TransportDisconnected:
                _ops.OnComplete();
                return;
        }

        if (data is not TransportData { Buffer: var buffer })
        {
            return;
        }

        var frames = _protocol.DecodeFrames(buffer);
        for (var i = 0; i < frames.Count; i++)
        {
            _protocol.ProcessFrame(frames[i]);
        }

        if (_protocol.GoAwayReceived && _protocol.HasInFlightRequests)
        {
            OnConnectionLost(_protocol.GoAwayLastStreamId);
            return;
        }

        if (frames.Count > 0)
        {
            ResetKeepAliveTimer();
        }
    }

    public void OnUpstreamFinished()
    {
        if (IsReconnecting)
        {
            _ops.OnFail(new HttpRequestException("TurboHTTP: HTTP/2 transport closed during reconnect."));
            return;
        }

        _ops.OnComplete();
    }

    public void OnTimerFired(string name)
    {
        switch (name)
        {
            case KeepAlivePingTimerKey:
            {
                var policy = _options.Http2.KeepAlivePingPolicy;
                if (policy == HttpKeepAlivePingPolicy.WithActiveRequests && !_protocol.HasInFlightRequests)
                {
                    return;
                }

                _protocol.SendKeepAlivePing();
                ScheduleKeepAlivePingTimeout();
                break;
            }
            case KeepAlivePingTimeoutKey:
            {
                if (_protocol.IsKeepAliveTimedOut(_options.Http2.KeepAlivePingTimeout))
                {
                    _ops.OnWarning("Keep-alive PING timeout — closing connection.");
                    if (_protocol.HasInFlightRequests)
                    {
                        OnConnectionLost(lastStreamId: 0);
                    }
                    else
                    {
                        _ops.OnComplete();
                    }
                }

                break;
            }
        }
    }

    public void Cleanup()
    {
        _protocol.Cleanup();
    }

    private void OnConnectionLost(int lastStreamId)
    {
        ClassifyStreamsForReplay(lastStreamId);
        _protocol.ReleaseAllStreamState();
        _protocol.ResetConnectionState();

        _transportOptions ??= OptionsFactory.Build(_protocol.Endpoint, _options);

        IsReconnecting = true;
        _reconnectAttempts = 1;
        _ops.OnOutbound(new ConnectTransport(_transportOptions));
    }

    private void ClassifyStreamsForReplay(int lastStreamId)
    {
        foreach (var (streamId, request) in _protocol.GetCorrelationMap())
        {
            if (IsStreamSafeToReplay(streamId, request, lastStreamId))
            {
                _reconnectBuffer.Add(request);
            }
            else
            {
                _ops.OnWarning(
                    $"TurboHTTP: Dropping non-idempotent or partially-responded request {request.Method} {request.RequestUri} on reconnect.");
                request.Dispose();
            }
        }
    }

    private bool IsStreamSafeToReplay(int streamId, HttpRequestMessage request, int lastStreamId)
    {
        if (streamId > lastStreamId)
        {
            return true;
        }

        return IsIdempotentMethod(request.Method) && !_protocol.HasReceivedHeaders(streamId);
    }

    private void OnConnectionRestored()
    {
        IsReconnecting = false;
        _reconnectAttempts = 0;

        var preface = _protocol.TryBuildPreface();
        if (preface is not null)
        {
            _ops.OnOutbound(preface);
        }

        var toReplay = ArrayPool<HttpRequestMessage>.Shared.Rent(_reconnectBuffer.Count);
        var replayCount = _reconnectBuffer.Count;
        _reconnectBuffer.CopyTo(toReplay);
        _reconnectBuffer.Clear();

        for (var i = 0; i < replayCount; i++)
        {
            _protocol.EncodeRequest(toReplay[i]);
        }

        ArrayPool<HttpRequestMessage>.Shared.Return(toReplay, true);

        ScheduleKeepAlivePing();
    }

    private void OnReconnectAttemptFailed()
    {
        if (_reconnectAttempts >= _options.Http2.MaxReconnectAttempts)
        {
            _ops.OnFail(new HttpRequestException("TurboHTTP: HTTP/2 reconnect failed after max attempts."));
            return;
        }

        _reconnectAttempts++;
        _ops.OnOutbound(new ConnectTransport(_transportOptions!));
    }

    private static bool IsIdempotentMethod(HttpMethod method)
        => method == HttpMethod.Get
           || method == HttpMethod.Head
           || method == HttpMethod.Options
           || method == HttpMethod.Trace
           || method == HttpMethod.Delete
           || method == HttpMethod.Put;

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
