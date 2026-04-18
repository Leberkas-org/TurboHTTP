using System.Buffers;
using System.Security.Cryptography.X509Certificates;
using TurboHTTP.Internal;
using TurboHTTP.Protocol.Http3.Qpack;
using TurboHTTP.Streams;
using TurboHTTP.Streams.Stages;

namespace TurboHTTP.Protocol.Http3;

/// <summary>
/// Encapsulates all HTTP/3 connection protocol logic — frame decoding, request encoding,
/// response assembly, QPACK feedback, SETTINGS, GOAWAY, push control, stream lifecycle,
/// idle timeout, and reconnection.
/// Calls back into <see cref="IStageOperations"/> for responses, outbound items, and warnings.
/// Mirrors the HTTP/2 <see cref="Http2.StateMachine"/> pattern.
/// <para>
/// Per-stream state and response assembly is delegated to <see cref="StreamManager"/>;
/// QPACK instruction streams to <see cref="QpackStreamHandler"/>.
/// </para>
/// </summary>
internal sealed class StateMachine : IDisposable
{
    private static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// HTTP methods that are safe for 0-RTT early data per RFC 9114 §A.1.
    /// </summary>
    private static readonly HashSet<HttpMethod> IdempotentMethods =
    [
        HttpMethod.Get,
        HttpMethod.Head,
        HttpMethod.Options,
        HttpMethod.Trace,
        HttpMethod.Delete,
    ];

    private readonly Http3EngineOptions _options;
    private readonly IStageOperations _ops;

    private readonly RequestEncoder _requestEncoder;
    private readonly ResponseDecoder _responseDecoder;
    private readonly QpackStreamHandler _qpackHandler;
    private readonly StreamManager _streamManager;

    // Reconnection
    private readonly List<Http3Frame> _reconnectBuffer = [];
    private int _reconnectAttempts;

    // Preface tracking
    private bool _controlPrefaceSent;

    /// <summary>Whether a new request can be accepted (no GOAWAY + not reconnecting + concurrency budget).</summary>
    public bool CanAcceptRequest => !Connection.GoAwayReceived && !IsReconnecting && Tracker.CanOpenStream();

    /// <summary>Whether the connection is currently in the reconnection phase.</summary>
    public bool IsReconnecting { get; private set; }

    /// <summary>Number of frames buffered for replay on reconnection.</summary>
    public int ReconnectBufferCount => _reconnectBuffer.Count;

    /// <summary>Whether a response was produced during the most recent ProcessFrame call.</summary>
    public bool ResponseProduced => _streamManager.ResponseProduced;

    /// <summary>Whether there are in-flight requests awaiting responses.</summary>
    public bool HasInFlightRequests => _streamManager.HasInFlightRequests;

    /// <summary>The current connection endpoint.</summary>
    public RequestEndpoint Endpoint { get; private set; }

    /// <summary>The underlying stream tracker for stream ID allocation and concurrency.</summary>
    internal StreamTracker Tracker { get; }

    /// <summary>The underlying connection state for idle timeout and settings inspection.</summary>
    internal ConnectionState Connection { get; }

    /// <summary>The QPACK table synchronization coordinator.</summary>
    internal QpackTableSync TableSync { get; }

    public StateMachine(Http3EngineOptions options, IStageOperations ops)
    {
        _options = options;
        _ops = ops;
        // RFC 9204 §3.2.3: the encoder MUST NOT use the dynamic table until the
        // peer has advertised a non-zero SETTINGS_QPACK_MAX_TABLE_CAPACITY.
        // We start static-only (0) and will update after receiving peer SETTINGS.
        TableSync = new QpackTableSync(
            encoderMaxCapacity: 0,
            decoderMaxCapacity: 4096,
            maxBlockedStreams: options.QpackBlockedStreams);
        _requestEncoder = new RequestEncoder(TableSync);
        _responseDecoder = new ResponseDecoder(TableSync);
        _qpackHandler = new QpackStreamHandler(ops, _requestEncoder, _responseDecoder, TableSync);
        _streamManager = new StreamManager(ops, _responseDecoder, TableSync)
        {
            FlushDecoderInstructionsCallback = _ => FlushDecoderInstructions(),
            OnStreamClosedCallback = OnStreamClosed
        };
        Tracker = new StreamTracker();

        var idleTimeout = options.IdleTimeout == TimeSpan.Zero
            ? DefaultIdleTimeout
            : options.IdleTimeout;

        Connection = new ConnectionState(idleTimeout, options.AllowServerPush ? 100 : 0);
    }

    /// <summary>
    /// Builds the HTTP/3 control stream preface if not yet sent.
    /// Emits: stream type VarInt(0x00) + SETTINGS frame + optional MAX_PUSH_ID.
    /// Returns null if already sent.
    /// </summary>
    public IOutputItem? TryBuildControlPreface()
    {
        if (_controlPrefaceSent)
        {
            return null;
        }

        _controlPrefaceSent = true;

        var settings = new Settings();
        var settingsFrame = settings.ToFrame();

        var streamTypeSize = QuicVarInt.EncodedLength((long)StreamType.Control);
        var frameSize = settingsFrame.SerializedSize;
        var totalSize = streamTypeSize + frameSize;

        Http3MaxPushIdFrame? maxPushIdFrame = null;
        if (_options.AllowServerPush)
        {
            maxPushIdFrame = new Http3MaxPushIdFrame(99);
            totalSize += maxPushIdFrame.SerializedSize;
        }

        using var owner = MemoryPool<byte>.Shared.Rent(totalSize);
        var span = owner.Memory.Span;

        var written = QuicVarInt.Encode((long)StreamType.Control, span);
        span = span[written..];
        settingsFrame.WriteTo(ref span);

        maxPushIdFrame?.WriteTo(ref span);

        var buf = Http3NetworkBuffer.Rent(totalSize);
        owner.Memory.Span[..totalSize].CopyTo(buf.FullMemory.Span);
        buf.Length = totalSize;
        buf.Key = Endpoint;
        buf.StreamType = Http3StreamType.Control;

        return buf;
    }

    /// <summary>
    /// Decodes a NetworkBuffer into HTTP/3 frames using a per-stream decoder.
    /// </summary>
    public IReadOnlyList<Http3Frame> DecodeServerData(NetworkBuffer buffer, long streamId)
    {
        return _streamManager.DecodeServerData(buffer, streamId);
    }

    /// <summary>
    /// Processes a single decoded HTTP/3 frame. Calls <see cref="IStageOperations"/>
    /// for responses, signals, and warnings. Sets <see cref="ResponseProduced"/> if a response was generated.
    /// Returns the frame if it should be forwarded for response assembly, or null if absorbed.
    /// </summary>
    public Http3Frame? ProcessFrame(Http3Frame frame)
    {
        Connection.RecordActivity();

        switch (frame)
        {
            case Http3SettingsFrame settings:
                HandleSettings(settings);
                return null;

            case Http3GoAwayFrame goAway:
                HandleGoAway(goAway);
                return null;

            case Http3PushPromiseFrame pushPromise:
                return HandlePushPromise(pushPromise);

            case Http3CancelPushFrame cancelPush:
                Connection.OnReceivedCancelPush(cancelPush);
                return null;

            case Http3MaxPushIdFrame:
                return null;

            case Http3HeadersFrame:
            default:
                return frame;
        }
    }

    /// <summary>
    /// Assembles a response from an HTTP/3 frame (HEADERS or DATA) on the given stream.
    /// Routes to per-stream state so multiple responses can be assembled concurrently.
    /// </summary>
    public void AssembleResponse(Http3Frame frame, long streamId)
    {
        _streamManager.AssembleResponse(frame, streamId, Endpoint);
    }

    /// <summary>
    /// Completes response assembly for a specific stream (QUIC FIN on request stream).
    /// </summary>
    public void FlushPendingResponse(long streamId)
    {
        _streamManager.FlushPendingResponse(streamId);
    }

    /// <summary>
    /// Completes all in-progress response assemblies (upstream finish / connection close).
    /// </summary>
    public void FlushPendingResponse()
    {
        _streamManager.FlushAllPendingResponses();
    }

    /// <summary>
    /// Encodes an outbound HTTP request into HTTP/3 frames and emits them via callbacks.
    /// Also handles QPACK encoder instructions and correlation tracking.
    /// Returns false if GOAWAY was received (request dropped).
    /// </summary>
    public bool EncodeRequest(HttpRequestMessage request)
    {
        if (Connection.GoAwayReceived)
        {
            _ops.OnWarning("RFC 9114 §5.2 — GOAWAY received; dropping outbound request.");
            return false;
        }

        return IsReconnecting ? BufferForReconnect(request) : EncodeAndEmit(request);
    }

    /// <summary>
    /// Processes bytes from the inbound QPACK decoder stream.
    /// </summary>
    public void ProcessQpackDecoderBytes(ReadOnlyMemory<byte> data)
    {
        _qpackHandler.ProcessDecoderBytes(data);
    }

    /// <summary>
    /// Processes bytes from the inbound QPACK encoder stream.
    /// Applies encoder instructions, resolves blocked streams, and emits responses.
    /// </summary>
    public void ProcessQpackEncoderBytes(ReadOnlyMemory<byte> data)
    {
        var resolved = _qpackHandler.ProcessEncoderBytes(data);
        FlushDecoderInstructions();
        _streamManager.ResolveBlockedStreams(resolved);
    }

    /// <summary>
    /// Serializes pending QPACK decoder instructions and emits them on the decoder stream.
    /// </summary>
    public void FlushDecoderInstructions()
    {
        _qpackHandler.FlushDecoderInstructions(Endpoint);
    }

    /// <summary>
    /// Serializes any pending QPACK encoder instructions and emits them on the encoder stream.
    /// </summary>
    public void FlushEncoderInstructions()
    {
        _qpackHandler.FlushEncoderInstructions(Endpoint);
    }

    /// <summary>
    /// Checks whether the idle timeout has expired with no active streams.
    /// </summary>
    public Http3GoAwayFrame? CheckIdleTimeout()
    {
        if (Connection.IsIdleTimeoutExpired() && Connection.ActiveStreamCount == 0)
        {
            _ops.OnWarning("RFC 9114 §5.1 — idle timeout expired with no active streams; sending GOAWAY.");
            return new Http3GoAwayFrame(0);
        }

        return null;
    }

    /// <summary>
    /// Evaluates whether this connection can be reused for a request to a different origin.
    /// </summary>
    public ConnectionReuseDecision EvaluateConnectionReuse(
        string targetScheme,
        string targetHost,
        int targetPort,
        X509Certificate2? serverCertificate)
    {
        var ep = Endpoint;
        return ConnectionReuseEvaluator.Evaluate(
            ep.Scheme,
            ep.Host,
            ep.Port,
            targetScheme,
            targetHost,
            targetPort,
            serverCertificate,
            Connection.GoAwayReceived);
    }

    /// <summary>Whether the idle timeout is disabled (timeout is zero).</summary>
    public bool IsTimeoutDisabled => Connection.IsTimeoutDisabled;

    /// <summary>Time remaining before the idle timeout expires.</summary>
    public TimeSpan TimeUntilExpiry() => Connection.TimeUntilExpiry();

    /// <summary>
    /// Called when the QUIC connection is lost.
    /// </summary>
    public void OnConnectionLost()
    {
        IsReconnecting = true;
        _reconnectAttempts = 1;

        _streamManager.DrainStreams();

        Tracker.Reset();
        Connection.Reset();
        _streamManager.ResetAllDecoders();
        _controlPrefaceSent = false;
        _qpackHandler.Reset();
        TableSync.Reset();
    }

    /// <summary>
    /// Called when a new QUIC connection is established after a loss.
    /// </summary>
    public void OnConnectionRestored()
    {
        IsReconnecting = false;
        _reconnectAttempts = 0;

        var preface = TryBuildControlPreface();
        if (preface is not null)
        {
            _ops.OnOutbound(preface);
        }

        ReplayBufferedFrames();
    }

    /// <summary>
    /// Called when a reconnect attempt fails.
    /// Returns true if another attempt should be made, false if max exceeded.
    /// </summary>
    public bool OnReconnectAttemptFailed()
    {
        if (_reconnectAttempts >= _options.MaxReconnectAttempts)
        {
            _ops.OnReconnectFailed();
            return false;
        }

        _reconnectAttempts++;
        return true;
    }

    /// <summary>
    /// Disposes owned resources.
    /// </summary>
    public void Dispose()
    {
        _streamManager.Dispose();
    }

    private bool BufferForReconnect(HttpRequestMessage request)
    {
        var frames = EncodeToFrames(request);
        foreach (var f in frames)
        {
            _reconnectBuffer.Add(f);
        }

        var reconnectStreamId = Tracker.AllocateStreamId();
        _streamManager.Correlate(reconnectStreamId, request);
        return true;
    }

    private bool EncodeAndEmit(HttpRequestMessage request)
    {
        var encoded = EncodeToFrames(request);

        var endpoint = request.RequestUri is not null
            ? RequestEndpoint.FromRequest(request)
            : RequestEndpoint.Default;

        if (Endpoint == default && endpoint != default)
        {
            Endpoint = endpoint;
        }

        var streamId = Tracker.AllocateStreamId();
        Tracker.OnStreamOpened(streamId);
        Connection.OnStreamOpened();

        _streamManager.Correlate(streamId, request);

        FlushEncoderInstructions();

        foreach (var f in encoded)
        {
            EmitSerializedFrame(f, streamId);
        }

        _ops.OnOutbound(new Http3EndOfRequestItem { Key = endpoint, StreamId = streamId });
        return true;
    }

    private IReadOnlyList<Http3Frame> EncodeToFrames(HttpRequestMessage request)
    {
        OriginValidator.Validate(request.RequestUri!, request.Method == HttpMethod.Connect);
        var frames = _requestEncoder.Encode(request);

        if (_options.AllowEarlyData && IdempotentMethods.Contains(request.Method))
        {
            foreach (var f in frames)
            {
                if (f is Http3HeadersFrame headers)
                {
                    headers.EarlyData = true;
                }
            }
        }

        return frames;
    }

    private void ReplayBufferedFrames()
    {
        var oldCorrelations = _streamManager.SnapshotAndClearCorrelations();
        var toReplay = _reconnectBuffer.ToList();
        _reconnectBuffer.Clear();

        var correlationIndex = 0;
        long currentReplayStreamId = -1;

        foreach (var frame in toReplay)
        {
            if (frame is Http3HeadersFrame)
            {
                currentReplayStreamId = Tracker.AllocateStreamId();
                Tracker.OnStreamOpened(currentReplayStreamId);
                Connection.OnStreamOpened();

                if (correlationIndex < oldCorrelations.Count)
                {
                    _streamManager.Correlate(currentReplayStreamId, oldCorrelations[correlationIndex++]);
                }
            }

            EmitSerializedFrame(frame, currentReplayStreamId);
        }
    }

    private void EmitSerializedFrame(Http3Frame frame, long streamId = -1)
    {
        var buf = Http3NetworkBuffer.Rent(frame.SerializedSize);
        var span = buf.FullMemory.Span;
        frame.WriteTo(ref span);
        buf.Length = frame.SerializedSize;
        buf.Key = Endpoint;

        if (streamId >= 0)
        {
            buf.StreamType = Http3StreamType.Request;
            buf.StreamId = streamId;
        }

        _ops.OnOutbound(buf);
    }

    private void OnStreamClosed(long streamId)
    {
        Tracker.OnStreamClosed(streamId);
        Connection.OnStreamClosed();
    }

    private void HandleSettings(Http3SettingsFrame settings)
    {
        try
        {
            Connection.OnRemoteSettings(settings);
            _ops.OnWarning(
                $"RFC 9114 §7.2.4 — remote SETTINGS received ({settings.Parameters.Count} parameters).");
        }
        catch (Http3Exception ex)
        {
            _ops.OnWarning($"SETTINGS error absorbed — {ex.Message}");
        }
    }

    private void HandleGoAway(Http3GoAwayFrame goAway)
    {
        try
        {
            Connection.OnServerGoAway(goAway);
            _ops.OnWarning(
                $"RFC 9114 §5.2 — GOAWAY received (streamId={goAway.StreamId}).");
        }
        catch (Http3Exception ex)
        {
            _ops.OnWarning($"GOAWAY error absorbed — {ex.Message}");
            Connection.GoAwayReceived = true;
        }
    }

    private Http3PushPromiseFrame? HandlePushPromise(Http3PushPromiseFrame pushPromise)
    {
        if (!_options.AllowServerPush)
        {
            var cancelFrame = new Http3CancelPushFrame(pushPromise.PushId);
            EmitSerializedFrame(cancelFrame);
            _ops.OnWarning(
                $"RFC 9114 §7.2.5 — push promise rejected (pushId={pushPromise.PushId}); AllowServerPush=false.");
            return null;
        }

        try
        {
            Connection.RecordPush();
        }
        catch (Http3Exception ex)
        {
            _ops.OnWarning($"Push limit exceeded — {ex.Message}");
            return null;
        }

        return pushPromise;
    }
}