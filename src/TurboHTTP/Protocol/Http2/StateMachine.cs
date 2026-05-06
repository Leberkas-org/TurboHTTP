using System.Buffers;
using Servus.Akka.Transport;
using TurboHTTP.Internal;
using TurboHTTP.Protocol.Http2.Hpack;
using TurboHTTP.Protocol.Semantics;
using TurboHTTP.Streams.Stages;

namespace TurboHTTP.Protocol.Http2;

/// <summary>
/// Encapsulates all HTTP/2 connection protocol logic — frame decoding, request encoding,
/// stream lifecycle, flow control, SETTINGS, PING, GOAWAY, and response assembly.
/// Calls back into <see cref="IStageOperations"/> for responses, outbound items, and warnings.
/// Outbound frames are serialized and emitted immediately via callbacks.
/// </summary>
internal sealed class StateMachine : IHttpStateMachine
{
    private const int MaxStatePoolCapacity = 1000;
    private readonly TurboClientOptions _options;

    private readonly IStageOperations _ops;

    private readonly StreamTracker _tracker;
    private readonly ConnectionState _connection;
    private readonly FrameDecoder _frameDecoder = new();
    private readonly ResponseDecoder _responseDecoder;
    private readonly RequestEncoder _requestEncoder;
    private readonly Dictionary<int, HttpRequestMessage> _correlationMap = new();

    private readonly Dictionary<int, StreamState> _streams = new();
    private readonly Stack<StreamState> _statePool;
    private TransportOptions? _transportOptions;
    private int _statePoolCapacity;

    private bool _prefaceSent;
    private readonly List<HttpRequestMessage> _reconnectBuffer = [];
    private int _reconnectAttempts;

    private bool _awaitingPingAck;
    private long _pingSentTimestamp;

    /// <summary>Whether the most recent ProcessFrame call produced a response.</summary>
    public bool ResponseProduced { get; private set; }

    /// <summary>Whether a new request stream can be opened (no GOAWAY + concurrency budget).</summary>
    public bool CanAcceptRequest => !_connection.GoAwayReceived && !IsReconnecting && _tracker.CanOpenStream();

    public bool IsReconnecting { get; private set; }

    public int ReconnectBufferCount => _reconnectBuffer.Count;
    public bool HasInFlightRequests => _correlationMap.Count > 0;

    /// <summary>The current connection endpoint.</summary>
    public RequestEndpoint Endpoint { get; private set; }

    private const string KeepAlivePingTimerKey = "keep-alive-ping";
    private const string KeepAlivePingTimeoutKey = "keep-alive-ping-timeout";

    private bool KeepAliveEnabled => _options.Http2.KeepAlivePingDelay != Timeout.InfiniteTimeSpan;

    public StateMachine(TurboClientOptions options, IStageOperations ops)
    {
        _options = options;
        _ops = ops;
        _tracker = new StreamTracker(1, options.Http2.MaxConcurrentStreams);
        _connection = new ConnectionState(options.Http2.InitialConnectionWindowSize,
            options.Http2.InitialStreamWindowSize);
        _requestEncoder = new RequestEncoder(useHuffman: true, maxFrameSize: 16_384);
        _statePoolCapacity = Math.Min(
            _tracker.MaxConcurrentStreams > 0 ? _tracker.MaxConcurrentStreams : 100,
            MaxStatePoolCapacity);
        _statePool = new Stack<StreamState>(_statePoolCapacity);
        _responseDecoder = new ResponseDecoder(new HpackDecoder());
    }

    public TransportData? TryBuildPreface()
    {
        if (_options.Http2.InitialConnectionWindowSize <= 0 || _prefaceSent)
        {
            return null;
        }

        _prefaceSent = true;
        var (prefaceOwner, prefaceLength) = PrefaceBuilder.Build(
            _options.Http2.InitialConnectionWindowSize,
            _options.Http2.HeaderTableSize,
            _options.Http2.MaxFrameSize);
        var prefaceBuf = TransportBuffer.Rent(prefaceLength);
        prefaceOwner.Memory.Span[..prefaceLength].CopyTo(prefaceBuf.FullMemory.Span);
        prefaceOwner.Dispose();
        prefaceBuf.Length = prefaceLength;
        return new TransportData(prefaceBuf);
    }

    /// <summary>
    /// Decodes a NetworkBuffer into HTTP/2 frames.
    /// </summary>
    public IReadOnlyList<Http2Frame> DecodeServerData(TransportBuffer buffer)
    {
        return _frameDecoder.Decode(buffer);
    }

    /// <summary>
    /// Processes a single decoded HTTP/2 frame. Calls <see cref="IStageOperations"/>
    /// for responses, signals, and warnings. Sets <see cref="ResponseProduced"/> if a response was generated.
    /// Returns false if a flow control violation occurred (caller should stop processing remaining frames).
    /// </summary>
    public bool ProcessFrame(Http2Frame frame)
    {
        ResponseProduced = false;

        switch (frame)
        {
            case SettingsFrame settings:
                HandleSettings(settings);
                break;

            case DataFrame data:
                if (!ProcessDataFrame(data))
                {
                    return false;
                }

                break;

            case HeadersFrame headers:
                HandleHeaders(headers);
                break;

            case ContinuationFrame cont:
                HandleContinuation(cont);
                break;

            case RstStreamFrame rst:
                CloseStream(rst.StreamId);
                break;

            case WindowUpdateFrame win:
                ProcessWindowUpdate(win);
                break;

            case PingFrame ping:
                HandlePing(ping);
                break;

            case GoAwayFrame goAway:
                ProcessGoAway(goAway);
                break;
        }

        return true;
    }

    private bool ProcessDataFrame(DataFrame data)
    {
        if (!HandleInboundData(data))
        {
            return false;
        }

        HandleData(data);

        if (data.EndStream)
        {
            CloseStream(data.StreamId);
        }

        return true;
    }

    private void ProcessWindowUpdate(WindowUpdateFrame win)
    {
        _connection.OnWindowUpdate(win);
        if (win.StreamId == 0)
        {
            _requestEncoder.UpdateConnectionWindow(win.Increment);
        }
        else
        {
            _requestEncoder.UpdateStreamWindow(win.StreamId, win.Increment);
        }
    }

    private void ProcessGoAway(GoAwayFrame goAway)
    {
        _connection.OnGoAway();
        _ops.OnWarning(
            $"TurboHTTP: GOAWAY received from {Endpoint.Host} — LastStreamId={goAway.LastStreamId}, ErrorCode={goAway.ErrorCode}. Reconnecting.");

        if (_correlationMap.Count > 0)
        {
            OnConnectionLost(goAway.LastStreamId);
        }
    }

    /// <summary>
    /// Encodes an outbound HTTP request into HTTP/2 frames and emits them via callbacks.
    /// Returns false if GOAWAY was received (request dropped).
    /// </summary>
    public bool EncodeRequest(HttpRequestMessage request)
    {
        var streamId = _tracker.AllocateStreamId();

        if (_connection.GoAwayReceived)
        {
            _ops.OnWarning(
                $"RFC 9113 §6.8 — GOAWAY received; dropping new request (stream {streamId}).");
            return false;
        }

        var endpoint = request.RequestUri is not null
            ? RequestEndpoint.FromRequest(request)
            : RequestEndpoint.Default;

        if (Endpoint == default && endpoint != default)
        {
            Endpoint = endpoint;
            _transportOptions = OptionsFactory.Build(Endpoint, _options);
            _ops.OnOutbound(new ConnectTransport(_transportOptions));
        }

        _correlationMap.TryAdd(streamId, request);

        if (request.RequestUri is null)
        {
            _tracker.OnStreamOpened(streamId);
            return true;
        }

        var frames = _requestEncoder.Encode(request, streamId);

        if (frames.Count == 0)
        {
            return true;
        }

        if (frames[0] is HeadersFrame headersFrame)
        {
            _tracker.OnStreamOpened(headersFrame.StreamId);
        }

        var totalSize = 0;
        for (var i = 0; i < frames.Count; i++)
        {
            totalSize += frames[i].SerializedSize;
        }

        var buf = TransportBuffer.Rent(totalSize);
        var span = buf.FullMemory.Span;
        for (var i = 0; i < frames.Count; i++)
        {
            frames[i].WriteTo(ref span);
        }

        buf.Length = totalSize;
        _ops.OnOutbound(new TransportData(buf));

        return true;
    }

    private void EmitFrame(Http2Frame frame)
    {
        var buf = TransportBuffer.Rent(frame.SerializedSize);
        var span = buf.FullMemory.Span;
        frame.WriteTo(ref span);
        buf.Length = frame.SerializedSize;
        _ops.OnOutbound(new TransportData(buf));
    }

    private void HandleSettings(SettingsFrame frame)
    {
        var result = _connection.OnRemoteSettings(frame);

        if (result.AckFrame is null)
        {
            return; // ACK frame — no action needed
        }

        if (result.MaxConcurrentStreamsChange is { } maxStreams)
        {
            _tracker.MaxConcurrentStreams = maxStreams;
            _statePoolCapacity = Math.Min(
                _tracker.MaxConcurrentStreams > 0 ? _tracker.MaxConcurrentStreams : 100,
                MaxStatePoolCapacity);
        }

        _requestEncoder.ApplyServerSettings(frame.Parameters);
        EmitFrame(result.AckFrame);
    }

    private bool HandleInboundData(DataFrame frame)
    {
        var result = _connection.OnInboundData(frame.StreamId, frame.Data.Length);

        if (result.IsConnectionViolation)
        {
            _ops.OnWarning("RFC 9113 §6.9 — connection flow control window exceeded. Triggering reconnect.");
            _ops.OnOutbound(new DisconnectTransport(DisconnectReason.Error));
            return false;
        }

        if (result.IsStreamViolation)
        {
            _ops.OnWarning(
                $"RFC 9113 §6.9 — stream {frame.StreamId} flow control window exceeded. Triggering reconnect.");
            _ops.OnOutbound(new DisconnectTransport(DisconnectReason.Error));
            return false;
        }

        if (result.ConnectionWindowUpdate is { } connUpdate)
        {
            EmitFrame(connUpdate);
        }

        if (result.StreamWindowUpdate is { } streamUpdate)
        {
            EmitFrame(streamUpdate);
        }

        return true;
    }

    private void HandlePing(PingFrame ping)
    {
        if (ping.IsAck)
        {
            _awaitingPingAck = false;
            return;
        }

        var ack = _connection.OnPing(ping);
        if (ack is not null)
        {
            EmitFrame(ack);
        }
    }

    /// <summary>
    /// Sends a keep-alive PING frame (RFC 9113 §6.7).
    /// Called by the stage timer when <see cref="Http2Options.KeepAlivePingDelay"/> is configured.
    /// </summary>
    public void SendKeepAlivePing()
    {
        if (_awaitingPingAck)
        {
            return;
        }

        _awaitingPingAck = true;
        _pingSentTimestamp = Environment.TickCount64;
        var data = BitConverter.GetBytes(_pingSentTimestamp);
        EmitFrame(new PingFrame(data, isAck: false));
    }

    /// <summary>
    /// Returns true if a keep-alive PING was sent but no ACK (or any frame) arrived
    /// within the configured timeout.
    /// </summary>
    public bool IsKeepAliveTimedOut(TimeSpan timeout)
    {
        if (!_awaitingPingAck)
        {
            return false;
        }

        var elapsed = Environment.TickCount64 - _pingSentTimestamp;
        return elapsed >= (long)timeout.TotalMilliseconds;
    }

    private void CloseStream(int streamId)
    {
        _tracker.OnStreamClosed(streamId);

        var windowUpdate = _connection.OnStreamClosed(streamId);
        if (windowUpdate is not null)
        {
            EmitFrame(windowUpdate);
        }
    }

    private StreamState RentState()
        => _statePool.TryPop(out var s) ? s : new StreamState();

    private void ReturnState(StreamState state)
    {
        state.Reset();
        if (_statePool.Count < _statePoolCapacity)
        {
            _statePool.Push(state);
        }
    }

    /// <summary>
    /// Called when the TCP connection is lost (GOAWAY or abrupt close) with in-flight requests.
    /// Classifies streams by LastStreamId and idempotency, buffers safe-to-replay requests,
    /// resets all connection state, and emits a ConnectItem (reconnect).
    /// </summary>
    public void OnConnectionLost(int lastStreamId)
    {
        ClassifyStreamsForReplay(lastStreamId);
        ReleaseAllStreamState();
        ResetConnectionState();

        IsReconnecting = true;
        _reconnectAttempts = 1;
        _ops.OnOutbound(new ConnectTransport(_transportOptions!));
    }

    private void ClassifyStreamsForReplay(int lastStreamId)
    {
        foreach (var (streamId, request) in _correlationMap)
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

        var hasReceivedHeaders = _streams.GetValueOrDefault(streamId)?.HasResponse ?? false;
        return IsIdempotentMethod(request.Method) && !hasReceivedHeaders;
    }

    private void ReleaseAllStreamState()
    {
        foreach (var (_, state) in _streams)
        {
            ReturnState(state);
        }

        _streams.Clear();
        _correlationMap.Clear();
    }

    private void ResetConnectionState()
    {
        _tracker.Reset();
        _connection.Reset(_options.Http2.InitialConnectionWindowSize, _options.Http2.InitialStreamWindowSize);
        _requestEncoder.ResetHpack();
        _responseDecoder.ResetHpack();
        _prefaceSent = false;
    }

    /// <summary>
    /// Called when ConnectedSignalItem arrives. Emits preface and replays buffered requests.
    /// </summary>
    public void OnConnectionRestored()
    {
        IsReconnecting = false;
        _reconnectAttempts = 0;

        // Build and emit preface (_prefaceSent = false was set in OnConnectionLost)
        var preface = TryBuildPreface();
        if (preface is not null)
        {
            _ops.OnOutbound(preface);
        }

        // Replay buffered requests with fresh stream IDs from reset tracker
        var toReplay = ArrayPool<HttpRequestMessage>.Shared.Rent(_reconnectBuffer.Count);
        var replayCount = _reconnectBuffer.Count;
        _reconnectBuffer.CopyTo(toReplay);
        _reconnectBuffer.Clear();

        for (var i = 0; i < replayCount; i++)
        {
            EncodeRequest(toReplay[i]);
        }

        ArrayPool<HttpRequestMessage>.Shared.Return(toReplay, true);
    }

    /// <summary>
    /// Called when a CloseSignalItem arrives while already reconnecting (reconnect attempt failed).
    /// Increments the attempt counter; emits a new ConnectItem (reconnect) or calls OnFail.
    /// </summary>
    public void OnReconnectAttemptFailed()
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

    private void HandleHeaders(HeadersFrame frame)
    {
        if (!_streams.TryGetValue(frame.StreamId, out var state))
        {
            state = RentState();
            _streams[frame.StreamId] = state;
        }

        state.AppendHeader(frame.HeaderBlockFragment.Span);

        if (!frame.EndHeaders)
        {
            return;
        }

        DecodeHeaders(frame.StreamId, frame.EndStream);
    }

    private void HandleContinuation(ContinuationFrame frame)
    {
        if (!_streams.TryGetValue(frame.StreamId, out var state))
        {
            _ops.OnWarning($"Received CONTINUATION for unknown stream {frame.StreamId} — dropping.");
            return;
        }

        state.AppendHeader(frame.HeaderBlockFragment.Span);

        if (frame.EndHeaders)
        {
            DecodeHeaders(frame.StreamId, false);
        }
    }

    private void HandleData(DataFrame frame)
    {
        if (!_streams.TryGetValue(frame.StreamId, out var state))
        {
            _ops.OnWarning($"Received DATA for unknown stream {frame.StreamId} — dropping.");
            return;
        }

        state.AppendBody(frame.Data.Span);

        if (!frame.EndStream)
        {
            return;
        }

        var response = _responseDecoder.CompleteDataResponse(state);

        if (_correlationMap.Remove(frame.StreamId, out var req))
        {
            response.RequestMessage = req;
        }

        var partialContentResult = PartialContentValidator.Validate(response);
        if (!partialContentResult.IsValid)
        {
            _ops.OnWarning(partialContentResult.ErrorMessage!);
        }

        ResponseProduced = true;
        _ops.OnResponse(response);

        _streams.Remove(frame.StreamId);
        ReturnState(state);
    }

    private void DecodeHeaders(int streamId, bool endStream)
    {
        if (!_streams.TryGetValue(streamId, out var state))
        {
            _ops.OnWarning($"DecodeHeaders called for unknown stream {streamId} — dropping.");
            return;
        }

        var response = _responseDecoder.DecodeHeaders(streamId, endStream, state);

        if (response is null)
        {
            return;
        }

        if (_correlationMap.Remove(streamId, out var req))
        {
            response.RequestMessage = req;
        }

        var partialContentResult = PartialContentValidator.Validate(response);
        if (!partialContentResult.IsValid)
        {
            _ops.OnWarning(partialContentResult.ErrorMessage!);
        }

        ResponseProduced = true;
        _ops.OnResponse(response);

        _streams.Remove(streamId);
        ReturnState(state);
    }

    /// <summary>
    /// Cleanup resources: close all streams, clear buffers, reset state, dispose responses.
    /// Called during stage shutdown via PostStop.
    /// </summary>
    public void Cleanup()
    {
        ReleaseAllStreamState();
        _statePool.Clear();
    }

    // --- IHttpStateMachine explicit implementation ---

    void IHttpStateMachine.PreStart()
    {
        var preface = TryBuildPreface();
        if (preface is not null)
        {
            _ops.OnOutbound(preface);
        }
    }

    void IHttpStateMachine.OnRequest(HttpRequestMessage request)
    {
        EncodeRequest(request);
    }

    void IHttpStateMachine.DecodeServerData(ITransportInbound data)
    {
        switch (data)
        {
            case TransportConnected:
            {
                OnConnectionRestored();
                EmitPreface();
                ScheduleKeepAlivePing();
                return;
            }

            case TransportDisconnected when IsReconnecting:
            {
                OnReconnectAttemptFailed();
                return;
            }

            case TransportDisconnected when HasInFlightRequests:
            {
                OnConnectionLost(lastStreamId: 0);
                return;
            }

            case TransportDisconnected:
            {
                _ops.OnComplete();
                return;
            }
        }

        if (data is not TransportData { Buffer: var buffer })
        {
            return;
        }

        var frames = DecodeServerData(buffer);

        var anyProcessed = false;
        for (var i = 0; i < frames.Count; i++)
        {
            var frame = frames[i];
            anyProcessed = true;
            var ok = ProcessFrame(frame);
            if (!ok)
            {
                break;
            }
        }

        if (anyProcessed)
        {
            ResetKeepAliveTimer();
        }
    }

    void IHttpStateMachine.OnUpstreamFinished()
    {
        if (IsReconnecting)
        {
            _ops.OnFail(new HttpRequestException("TurboHTTP: HTTP/2 transport closed during reconnect."));
            return;
        }

        _ops.OnComplete();
    }

    void IHttpStateMachine.OnTimerFired(string name)
    {
        switch (name)
        {
            case KeepAlivePingTimerKey:
            {
                var policy = _options.Http2.KeepAlivePingPolicy;
                if (policy == HttpKeepAlivePingPolicy.WithActiveRequests && !HasInFlightRequests)
                {
                    return;
                }

                SendKeepAlivePing();
                ScheduleKeepAlivePingTimeout();
                break;
            }
            case KeepAlivePingTimeoutKey:
            {
                if (IsKeepAliveTimedOut(_options.Http2.KeepAlivePingTimeout))
                {
                    _ops.OnWarning("Keep-alive PING timeout — closing connection.");
                    if (HasInFlightRequests)
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

    void IHttpStateMachine.Cleanup()
    {
        Cleanup();
    }

    private void EmitPreface()
    {
        var preface = TryBuildPreface();
        if (preface is not null)
        {
            _ops.OnOutbound(preface);
        }
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