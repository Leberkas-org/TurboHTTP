using Servus.Akka.Transport;
using TurboHTTP.Client;
using TurboHTTP.Internal;
using TurboHTTP.Protocol.Multiplexed;
using TurboHTTP.Protocol.Syntax.Http3.Options;
using TurboHTTP.Streams.Stages.Client;
using static Servus.Core.Servus;

namespace TurboHTTP.Protocol.Syntax.Http3.Client;

internal sealed class Http3ClientStateMachine : IClientStateMachine
{
    private static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromSeconds(30);

    private readonly TurboClientOptions _options;
    private readonly IClientStageOperations _ops;
    private TransportOptions? _transportOptions;

    private readonly Http3ClientSessionManager _clientSession;
    private readonly ReconnectionManager _reconnect;

    private readonly Server.ServerStreamResolver _serverStreamResolver;

    public bool CanAcceptRequest => !Connection.GoAwayReceived && !IsReconnecting && _clientSession.CanOpenStream;

    public bool IsReconnecting => _reconnect.IsReconnecting;

    public int ReconnectBufferCount => _reconnect.BufferedCount;

    public bool HasInFlightRequests => _clientSession.HasInFlightRequests;

    public RequestEndpoint Endpoint => _clientSession.Endpoint;

    private ConnectionState Connection { get; }

    public Http3ClientStateMachine(TurboClientOptions options, IClientStageOperations ops)
    {
        _options = options;
        _ops = ops;

        var shared = SharedHttpOptions.Default with
        {
            MaxBufferedBodySize = options.MaxBufferedBodySize,
            MaxStreamedBodySize = options.MaxStreamedBodySize,
        };

        var encoderOpts = new Http3ClientEncoderOptions
        {
            QpackMaxTableCapacity = options.Http3.QpackMaxTableCapacity,
            QpackBlockedStreams = options.Http3.QpackBlockedStreams,
            Shared = shared,
        };

        var decoderOpts = new Http3ClientDecoderOptions
        {
            MaxConcurrentStreams = options.Http3.MaxConcurrentStreams,
            MaxFieldSectionSize = options.Http3.MaxFieldSectionSize,
            Shared = shared,
        };

        _clientSession = new Http3ClientSessionManager(encoderOpts, decoderOpts, options, ops);
        _reconnect = new ReconnectionManager(options.Http3.MaxReconnectAttempts, options.Http3.MaxReconnectBufferSize);
        _serverStreamResolver = new Server.ServerStreamResolver
        {
            OnPushStreamDetected = HandleIncomingPushStream
        };

        var idleTimeout = options.Http3.IdleTimeout == TimeSpan.Zero
            ? DefaultIdleTimeout
            : options.Http3.IdleTimeout;

        Connection = new ConnectionState(idleTimeout);
    }

    public void PreStart()
    {
        _clientSession.OpenCriticalStreams();
        ScheduleIdleCheck();
    }

    public void OnRequest(HttpRequestMessage request)
    {
        if (Connection.GoAwayReceived)
        {
            Tracing.For("Protocol").Warning(this, "RFC 9114 §5.2 — GOAWAY received; dropping outbound request.");
            return;
        }

        if (IsReconnecting)
        {
            BufferForReconnect(request);
            return;
        }

        _clientSession.EncodeRequest(request);
    }

    public void DecodeServerData(ITransportInbound data)
    {
        switch (data)
        {
            case TransportConnected:
                {
                    _clientSession.OnTransportConnected();
                    OnConnectionRestored();
                    return;
                }

            case TransportDisconnected when IsReconnecting:
                {
                    OnReconnectAttemptFailed();
                    return;
                }

            case TransportDisconnected when HasInFlightRequests:
                {
                    OnConnectionLost();
                    return;
                }

            case TransportDisconnected:
                {
                    _clientSession.OnTransportDisconnected();
                    return;
                }

            case ServerStreamAccepted { Id: var id }:
                {
                    _serverStreamResolver.OnServerStreamOpened(id);
                    return;
                }

            case StreamOpened { Id: var openedId }:
                {
                    return;
                }

            case StreamReadCompleted { Id.Value: >= 0 } readCompleted:
                {
                    _clientSession.FlushPendingResponse(readCompleted.Id.Value);
                    return;
                }

            case StreamReadCompleted { Id: var srcId }:
                {
                    return;
                }

            case StreamClosed { Id.Value: >= 0 } streamClosed:
                {
                    Connection.OnStreamClosed();
                    if (streamClosed.Reason == DisconnectReason.Error)
                    {
                        OnConnectionLost();
                    }
                    else
                    {
                        _clientSession.FlushPendingResponse(streamClosed.Id.Value);
                    }

                    return;
                }

            case StreamClosed:
                {
                    _clientSession.FlushAllPendingResponses();
                    return;
                }

            case MultiplexedData multiplexed:
                {
                    HandleTaggedStreamData(multiplexed);
                    return;
                }

            case TransportData rawData:
                {
                    Tracing.For("Protocol").Warning(this,
                        "Received untagged TransportData — dropping to prevent stream ID misrouting.");
                    rawData.Buffer.Dispose();
                    return;
                }
        }
    }

    public void OnUpstreamFinished()
    {
        _clientSession.FlushAllPendingResponses();

        if (IsReconnecting)
        {
            Tracing.For("Protocol").Debug(this,
                "HTTP/3 transport closed during reconnect — discarding in-flight request(s).");
            var correlations = _clientSession.SnapshotAndClearCorrelations();
            if (correlations.Count > 0)
            {
                RequestFault.FailAll(correlations,
                    new HttpRequestException("HTTP/3 transport closed during reconnect."));
            }
        }
    }

    public void OnTimerFired(string name)
    {
        if (name != "idle-timeout-check")
        {
            return;
        }

        var goAway = CheckIdleTimeout();
        if (goAway is not null)
        {
            var buf = TransportBuffer.Rent(goAway.SerializedSize);
            var span = buf.FullMemory.Span;
            goAway.WriteTo(ref span);
            buf.Length = goAway.SerializedSize;
            _ops.OnOutbound(new MultiplexedData(buf, CriticalStreamId.Control));
            return;
        }

        ScheduleIdleCheck();
    }

    public void OnBodyMessage(object msg)
    {
        _clientSession.OnBodyMessage(msg);
    }

    public void Cleanup()
    {
        _clientSession.Cleanup();
    }


    private Http3Frame? ProcessFrame(Http3Frame frame)
    {
        Connection.RecordActivity();

        switch (frame)
        {
            case SettingsFrame settings:
                HandleSettings(settings);
                return null;

            case GoAwayFrame goAway:
                HandleGoAway(goAway);
                return null;

            case PushPromiseFrame pushPromise:
                return HandlePushPromise(pushPromise);

            case CancelPushFrame cancelPush:
                Connection.OnReceivedCancelPush(cancelPush);
                return null;

            case MaxPushIdFrame:
                return null;

            case HeadersFrame:
            default:
                return frame;
        }
    }

    private GoAwayFrame? CheckIdleTimeout()
    {
        if (!Connection.IsIdleTimeoutExpired() || Connection.ActiveStreamCount != 0) return null;
        Tracing.For("Protocol").Info(this,
            "RFC 9114 §5.1 — idle timeout expired with no active streams; sending GOAWAY.");
        return new GoAwayFrame(0);
    }

    private void OnConnectionLost()
    {
        var correlations = _clientSession.GetCorrelationMap().Values.ToList();
        _reconnect.OnConnectionLost(correlations);

        _clientSession.DrainStreams();
        _clientSession.ResetConnectionState();

        Connection.Reset();
        _serverStreamResolver.Reset();

        _transportOptions ??= OptionsFactory.Build(Endpoint, _options);
        _ops.OnOutbound(new ConnectTransport(_transportOptions));
    }

    private void OnConnectionRestored()
    {
        var preface = _clientSession.TryBuildControlPreface();
        if (preface is not null)
        {
            _ops.OnOutbound(preface);
        }

        var toReplay = _reconnect.OnConnectionRestored();
        for (var i = 0; i < toReplay.Count; i++)
        {
            _clientSession.EncodeRequest(toReplay[i]);
        }
    }

    private void OnReconnectAttemptFailed()
    {
        if (!_reconnect.OnReconnectAttemptFailed())
        {
            Tracing.For("Protocol").Info(this, "HTTP/3 reconnect failed after max attempts");
            _reconnect.FailAllBuffered(new HttpRequestException("HTTP/3 reconnect failed after max attempts."));
            return;
        }

        _ops.OnOutbound(new ConnectTransport(_transportOptions!));
    }

    private void ScheduleIdleCheck()
    {
        if (Connection.IsTimeoutDisabled)
        {
            return;
        }

        var remaining = Connection.TimeUntilExpiry();
        var checkInterval = remaining > TimeSpan.Zero ? remaining : TimeSpan.FromSeconds(1);
        _ops.OnScheduleTimer("idle-timeout-check", checkInterval);
    }

    private void BufferForReconnect(HttpRequestMessage request)
    {
        if (!_reconnect.Buffer(request))
        {
            request.Fail(new HttpRequestException("HTTP/3 reconnect buffer full."));
        }
    }


    private void HandleSettings(SettingsFrame settings)
    {
        try
        {
            Connection.OnRemoteSettings(settings);
            Tracing.For("Protocol").Info(this, "RFC 9114 §7.2.4 — remote SETTINGS received ({0} parameters).",
                settings.Parameters.Count);

            _clientSession.HandleSettings(settings);
        }
        catch (HttpProtocolException ex)
        {
            Tracing.For("Protocol").Warning(this, "SETTINGS error absorbed — {0}", ex.Message);
        }
    }

    private void HandleGoAway(GoAwayFrame goAway)
    {
        try
        {
            Connection.OnServerGoAway(goAway);
            Tracing.For("Protocol").Info(this, "RFC 9114 §5.2 — GOAWAY received (streamId={0}).", goAway.StreamId);
        }
        catch (HttpProtocolException ex)
        {
            Tracing.For("Protocol").Warning(this, "GOAWAY error absorbed — {0}", ex.Message);
            Connection.GoAwayReceived = true;
        }
    }

    private PushPromiseFrame? HandlePushPromise(PushPromiseFrame pushPromise)
    {
        var cancelFrame = new CancelPushFrame(pushPromise.PushId);
        var buf = TransportBuffer.Rent(cancelFrame.SerializedSize);
        var span = buf.FullMemory.Span;
        cancelFrame.WriteTo(ref span);
        buf.Length = cancelFrame.SerializedSize;
        _ops.OnOutbound(new MultiplexedData(buf, CriticalStreamId.Control));
        Tracing.For("Protocol").Info(this,
            "RFC 9114 §7.2.5 — push promise rejected (pushId={0}); server push not supported", pushPromise.PushId);
        return null;
    }

    private void HandleIncomingPushStream(long quicStreamId, ReadOnlySpan<byte> remaining)
    {
        long pushId = -1;
        if (QuicVarInt.TryDecode(remaining, out var id, out _))
        {
            pushId = id;
        }

        if (pushId >= 0)
        {
            var cancel = new CancelPushFrame(pushId);
            var buf = TransportBuffer.Rent(cancel.SerializedSize);
            var span = buf.FullMemory.Span;
            cancel.WriteTo(ref span);
            buf.Length = cancel.SerializedSize;
            _ops.OnOutbound(new MultiplexedData(buf, CriticalStreamId.Control));
        }

        _ops.OnOutbound(new ResetStream(quicStreamId));
        Tracing.For("Protocol").Info(this,
            "RFC 9114 §4.6 — push stream {0} (pushId={1}) reset (push response delivery not implemented)", quicStreamId,
            pushId);
    }

    private void HandleTaggedStreamData(MultiplexedData multiplexed)
    {
        var resolved = _serverStreamResolver.Resolve(multiplexed.StreamId, multiplexed.Buffer);

        if (resolved.Buffer is null)
        {
            return;
        }

        switch (resolved.LogicalStreamId)
        {
            case CriticalStreamId.QpackDecoderId:
                {
                    _clientSession.ProcessQpackDecoderBytes(resolved.Buffer.Memory);
                    resolved.Buffer.Dispose();
                    return;
                }
            case CriticalStreamId.QpackEncoderId:
                {
                    _clientSession.ProcessQpackEncoderBytes(resolved.Buffer.Memory);
                    resolved.Buffer.Dispose();
                    return;
                }
            case CriticalStreamId.ControlId:
                {
                    ProcessFrameData(resolved.Buffer, CriticalStreamId.ControlId);
                    return;
                }
            default:
                {
                    ProcessFrameData(resolved.Buffer, resolved.LogicalStreamId);
                    return;
                }
        }
    }

    private void ProcessFrameData(TransportBuffer buffer, long streamId)
    {
        var frames = _clientSession.DecodeServerData(buffer, streamId);

        for (var i = 0; i < frames.Count; i++)
        {
            var frame = frames[i];
            var forwarded = ProcessFrame(frame);
            if (forwarded is not null)
            {
                _clientSession.AssembleResponse(forwarded, streamId);
            }
        }
    }
}