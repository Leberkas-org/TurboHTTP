using TurboHTTP.Internal;
using TurboHTTP.Protocol.Http2.Hpack;
using TurboHTTP.Protocol.Http11;

namespace TurboHTTP.Protocol.Http2;

/// <summary>
/// Callback interface for the stage Logic to receive protocol effects from the state machine.
/// The stage implements this and translates calls to Akka Push/Emit/Log operations.
/// </summary>
public interface IHttp2StageOperations
{
    void OnResponse(HttpResponseMessage response);
    void OnOutbound(IOutputItem item);
    void OnWarning(string message);
    void OnReconnectFailed();
}

/// <summary>
/// Immutable configuration for an HTTP/2 connection.
/// </summary>
public sealed record Http2ConnectionConfig(
    int InitialRecvWindowSize = 1_048_576,
    int MaxConcurrentStreams = 100,
    int MaxHeaderSize = 16 * 1024,
    int MaxTotalHeaderSize = 64 * 1024,
    int MaxReconnectAttempts = 3);

/// <summary>
/// Encapsulates all HTTP/2 connection protocol logic — frame decoding, request encoding,
/// stream lifecycle, flow control, SETTINGS, PING, GOAWAY, and response assembly.
/// Calls back into <see cref="IHttp2StageOperations"/> for responses, outbound items, and warnings.
/// Outbound frames are serialized and emitted immediately via callbacks.
/// </summary>
public sealed class StateMachine
{
    private const int MaxStatePoolCapacity = 1000;

    private readonly Http2ConnectionConfig _config;
    private readonly IHttp2StageOperations _ops;

    private readonly StreamTracker _tracker;
    private readonly ConnectionState _connection;
    private readonly FrameDecoder _frameDecoder = new();
    private readonly ResponseDecoder _responseDecoder;
    private readonly RequestEncoder _requestEncoder = new();
    private readonly Dictionary<int, HttpRequestMessage> _correlationMap = new();

    private readonly Dictionary<int, StreamState> _streams = new();
    private readonly Stack<StreamState> _statePool;
    private int _statePoolCapacity;

    private bool _prefaceSent;
    private readonly List<HttpRequestMessage> _reconnectBuffer = [];
    private bool _reconnecting;
    private int _reconnectAttempts;

    /// <summary>Whether the most recent ProcessFrame call produced a response.</summary>
    public bool ResponseProduced { get; private set; }

    /// <summary>Whether a new request stream can be opened (no GOAWAY + concurrency budget).</summary>
    public bool CanAcceptRequest => !_connection.GoAwayReceived && !_reconnecting && _tracker.CanOpenStream();

    public bool IsReconnecting => _reconnecting;
    public int ReconnectBufferCount => _reconnectBuffer.Count;
    public bool HasInFlightRequests => _correlationMap.Count > 0;

    /// <summary>The current connection endpoint.</summary>
    public RequestEndpoint Endpoint { get; private set; }

    public StateMachine(Http2ConnectionConfig config, IHttp2StageOperations ops)
    {
        _config = config;
        _ops = ops;
        _tracker = new StreamTracker(1, config.MaxConcurrentStreams);
        _connection = new ConnectionState(config.InitialRecvWindowSize);
        _statePoolCapacity = Math.Min(
            _tracker.MaxConcurrentStreams > 0 ? _tracker.MaxConcurrentStreams : 100,
            MaxStatePoolCapacity);
        _statePool = new Stack<StreamState>(_statePoolCapacity);
        _responseDecoder = new ResponseDecoder(
            new HpackDecoder(), config.MaxHeaderSize, config.MaxTotalHeaderSize);
    }

    // ─── Preface (RFC 9113 §3.4) ───

    /// <summary>
    /// Builds the connection preface if not yet sent. Returns null if already sent or disabled.
    /// </summary>
    public NetworkBuffer? TryBuildPreface()
    {
        if (_config.InitialRecvWindowSize <= 0 || _prefaceSent)
        {
            return null;
        }

        _prefaceSent = true;
        var (prefaceOwner, prefaceLength) = PrefaceBuilder.Build(_config.InitialRecvWindowSize);
        var prefaceBuf = NetworkBuffer.Rent(prefaceLength);
        prefaceOwner.Memory.Span[..prefaceLength].CopyTo(prefaceBuf.FullMemory.Span);
        prefaceOwner.Dispose();
        prefaceBuf.Length = prefaceLength;
        return prefaceBuf;
    }

    // ─── Server data processing ───

    /// <summary>
    /// Decodes a NetworkBuffer into HTTP/2 frames.
    /// </summary>
    public IReadOnlyList<Http2Frame> DecodeServerData(NetworkBuffer buffer)
    {
        return _frameDecoder.Decode(buffer);
    }

    /// <summary>
    /// Processes a single decoded HTTP/2 frame. Calls <see cref="IHttp2StageOperations"/>
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
                if (!HandleInboundData(data))
                {
                    return false;
                }

                HandleData(data);

                if (data.EndStream)
                {
                    CloseStream(data.StreamId);
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
                _connection.OnWindowUpdate(win);
                if (win.StreamId == 0)
                {
                    _requestEncoder.UpdateConnectionWindow(win.Increment);
                }
                else
                {
                    _requestEncoder.UpdateStreamWindow(win.StreamId, win.Increment);
                }

                break;

            case PingFrame ping:
                HandlePing(ping);
                break;

            case GoAwayFrame goAway:
                _connection.OnGoAway();
                _ops.OnWarning(
                    $"TurboHTTP: GOAWAY received from {Endpoint.Host} — LastStreamId={goAway.LastStreamId}, ErrorCode={goAway.ErrorCode}. Reconnecting.");
                // Only reconnect if there are in-flight requests to replay; otherwise let the connection drain naturally.
                if (_correlationMap.Count > 0)
                {
                    BufferOrphanedRequests(goAway.LastStreamId);
                }

                break;
        }

        return true;
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
        }

        _correlationMap.TryAdd(streamId, request);

        if (request.RequestUri is null)
        {
            _tracker.OnStreamOpened(streamId);
            _ops.OnOutbound(new StreamAcquireItem { Key = Endpoint });
            return true;
        }

        var frames = _requestEncoder.Encode(request, streamId);
        var first = true;
        foreach (var frame in frames)
        {
            if (first)
            {
                first = false;

                if (frame is HeadersFrame headers)
                {
                    _tracker.OnStreamOpened(headers.StreamId);
                    _ops.OnOutbound(new StreamAcquireItem { Key = Endpoint });
                }
            }

            EmitFrame(frame);
        }

        return true;
    }

    private void EmitFrame(Http2Frame frame)
    {
        var buf = NetworkBuffer.Rent(frame.SerializedSize);
        var span = buf.FullMemory.Span;
        frame.WriteTo(ref span);
        buf.Length = frame.SerializedSize;
        buf.Key = Endpoint;
        _ops.OnOutbound(buf);
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
            _ops.OnOutbound(new MaxConcurrentStreamsItem(_tracker.MaxConcurrentStreams)
            {
                Key = Endpoint
            });
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
            var item = new ConnectionReuseItem(
                    ConnectionReuseDecision.Close("RFC 9113 §6.9: connection window exceeded"))
                { Key = Endpoint };
            _ops.OnOutbound(item);
            return false;
        }

        if (result.IsStreamViolation)
        {
            _ops.OnWarning(
                $"RFC 9113 §6.9 — stream {frame.StreamId} flow control window exceeded. Triggering reconnect.");
            var item = new ConnectionReuseItem(ConnectionReuseDecision.Close("RFC 9113 §6.9: stream window exceeded"))
                { Key = Endpoint };
            _ops.OnOutbound(item);
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
        var ack = _connection.OnPing(ping);
        if (ack is not null)
        {
            EmitFrame(ack);
        }
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

    // ─── Reconnect ───

    /// <summary>
    /// Called when GOAWAY or abrupt close is received with in-flight requests.
    /// Classifies streams by LastStreamId and idempotency, buffers safe-to-replay requests,
    /// resets all connection state, and emits a ReconnectItem.
    /// </summary>
    public void BufferOrphanedRequests(int lastStreamId)
    {
        foreach (var (streamId, request) in _correlationMap)
        {
            var streamState = _streams.GetValueOrDefault(streamId);
            var hasReceivedHeaders = streamState?.Response is not null;

            if (streamId > lastStreamId)
            {
                // Server never saw this stream — always safe to replay
                _reconnectBuffer.Add(request);
            }
            else if (IsIdempotentMethod(request.Method) && !hasReceivedHeaders)
            {
                // Server may have processed, but idempotent and no response started — replay
                _reconnectBuffer.Add(request);
            }
            else
            {
                // Non-idempotent or partial response received — cannot safely replay
                _ops.OnWarning(
                    $"TurboHTTP: Dropping non-idempotent or partially-responded request {request.Method} {request.RequestUri} on reconnect.");
                request.Dispose();
            }
        }

        // Release all stream state objects back to pool
        foreach (var (_, state) in _streams)
        {
            ReturnState(state);
        }

        _streams.Clear();
        _correlationMap.Clear();

        // Reset connection state for new connection
        _tracker.Reset();
        _connection.Reset(_config.InitialRecvWindowSize);
        _requestEncoder.ResetHpack();
        _responseDecoder.ResetHpack();
        _prefaceSent = false;

        _reconnecting = true;
        _reconnectAttempts = 1;
        _ops.OnOutbound(new ReconnectItem { Key = Endpoint });
    }

    /// <summary>
    /// Called when ConnectedSignalItem arrives. Emits preface and replays buffered requests.
    /// </summary>
    public void HandleConnectedSignal()
    {
        _reconnecting = false;
        _reconnectAttempts = 0;

        // Build and emit preface (_prefaceSent = false was set in BufferOrphanedRequests)
        var preface = TryBuildPreface();
        if (preface is not null)
        {
            _ops.OnOutbound(preface);
        }

        // Replay buffered requests with fresh stream IDs from reset tracker
        var toReplay = _reconnectBuffer.ToList();
        _reconnectBuffer.Clear();

        foreach (var request in toReplay)
        {
            EncodeRequest(request);
        }
    }

    /// <summary>
    /// Called when a CloseSignalItem arrives while already reconnecting (reconnect attempt failed).
    /// Increments the attempt counter; emits a new ReconnectItem or calls OnReconnectFailed.
    /// </summary>
    public void HandleReconnectAttempt()
    {
        if (_reconnectAttempts >= _config.MaxReconnectAttempts)
        {
            _ops.OnReconnectFailed();
            return;
        }

        _reconnectAttempts++;
        _ops.OnOutbound(new ReconnectItem { Key = Endpoint });
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

        ResponseProduced = true;
        _ops.OnResponse(response);

        _streams.Remove(streamId);
        ReturnState(state);
    }
}