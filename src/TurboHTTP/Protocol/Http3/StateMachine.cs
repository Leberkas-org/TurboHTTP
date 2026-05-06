using System.Buffers;
using System.Security.Cryptography.X509Certificates;
using Servus.Akka.Transport;
using TurboHTTP.Internal;
using TurboHTTP.Protocol.Http3.Qpack;
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
internal sealed class StateMachine : IHttpStateMachine, IDisposable
{
    private static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromSeconds(30);

    private readonly TurboClientOptions _options;
    private readonly IStageOperations _ops;
    private TransportOptions? _transportOptions;

    private readonly RequestEncoder _requestEncoder;
    private readonly ResponseDecoder _responseDecoder;
    private readonly QpackStreamHandler _qpackHandler;
    private readonly StreamManager _streamManager;

    // Reconnection
    private readonly List<Http3Frame> _reconnectBuffer = [];
    private int _reconnectAttempts;

    // Preface tracking
    private bool _controlPrefaceSent;

    // Transport connection tracking and pre-connect buffering
    // For HTTP/3, the transport (QUIC) is already connected when the stage starts,
    // so we begin in the connected state and bypass pre-connect buffering.
    private bool _transportConnected = true;
    private readonly List<ITransportOutbound> _preConnectBuffer = [];

    // Server-initiated stream mapping (QUIC stream ID → logical stream ID)
    private readonly Dictionary<long, long> _serverStreamMap = new();
    private readonly HashSet<long> _pendingStreamType = [];
    private readonly HashSet<long> _assignedCriticalStreams = [];

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

    /// <summary>
    /// Fails an in-flight request on the given stream due to a transport error.
    /// Returns true if a correlated request was found and failed.
    /// </summary>
    public bool FailInflightRequest(long streamId, Exception exception) =>
        _streamManager.FailInflightRequest(streamId, exception);

    /// <summary>The current connection endpoint.</summary>
    public RequestEndpoint Endpoint { get; private set; }

    /// <summary>The underlying stream tracker for stream ID allocation and concurrency.</summary>
    internal StreamTracker Tracker { get; }

    /// <summary>The underlying connection state for idle timeout and settings inspection.</summary>
    internal ConnectionState Connection { get; }

    /// <summary>The QPACK table synchronization coordinator.</summary>
    internal QpackTableSync TableSync { get; }

    public StateMachine(TurboClientOptions options, IStageOperations ops)
    {
        _options = options;
        _ops = ops;
        // RFC 9204 §3.2.3: the encoder MUST NOT use the dynamic table until the
        // peer has advertised a non-zero SETTINGS_QPACK_MAX_TABLE_CAPACITY.
        // The encoder starts at capacity 0; UpdateEncoderCapacity activates it
        // after receiving peer SETTINGS (see HandleSettings).
        TableSync = new QpackTableSync(
            encoderMaxCapacity: 0,
            decoderMaxCapacity: options.Http3.QpackMaxTableCapacity,
            maxBlockedStreams: options.Http3.QpackBlockedStreams,
            configuredEncoderLimit: options.Http3.QpackMaxTableCapacity);
        _requestEncoder = new RequestEncoder(TableSync);
        _responseDecoder = new ResponseDecoder(TableSync, options.Http3.MaxFieldSectionSize);
        _qpackHandler = new QpackStreamHandler(ops, _requestEncoder, _responseDecoder, TableSync);
        _streamManager = new StreamManager(ops, _responseDecoder, TableSync)
        {
            FlushDecoderInstructionsCallback = _ => FlushDecoderInstructions(),
            OnStreamClosedCallback = OnStreamClosed
        };
        Tracker = new StreamTracker(maxConcurrentStreams: options.Http3.MaxConcurrentStreams);

        var idleTimeout = options.Http3.IdleTimeout == TimeSpan.Zero
            ? DefaultIdleTimeout
            : options.Http3.IdleTimeout;

        Connection = new ConnectionState(idleTimeout);
    }

    /// <summary>
    /// Builds the HTTP/3 control stream preface if not yet sent.
    /// Emits: stream type VarInt(0x00) + SETTINGS frame + optional MAX_PUSH_ID.
    /// Returns null if already sent.
    /// </summary>
    public ITransportOutbound? TryBuildControlPreface()
    {
        if (_controlPrefaceSent)
        {
            return null;
        }

        _controlPrefaceSent = true;

        var settings = new Settings();
        settings.Set(Http3SettingsIdentifier.QpackMaxTableCapacity, _options.Http3.QpackMaxTableCapacity);
        settings.Set(Http3SettingsIdentifier.QpackBlockedStreams, _options.Http3.QpackBlockedStreams);
        settings.Set(Http3SettingsIdentifier.MaxFieldSectionSize, _options.Http3.MaxFieldSectionSize);
        var settingsFrame = settings.ToFrame();

        var streamTypeSize = QuicVarInt.EncodedLength((long)StreamType.Control);
        var frameSize = settingsFrame.SerializedSize;
        var totalSize = streamTypeSize + frameSize;

        using var owner = MemoryPool<byte>.Shared.Rent(totalSize);
        var span = owner.Memory.Span;

        var written = QuicVarInt.Encode((long)StreamType.Control, span);
        span = span[written..];
        settingsFrame.WriteTo(ref span);

        var buf = TransportBuffer.Rent(totalSize);
        owner.Memory.Span[..totalSize].CopyTo(buf.FullMemory.Span);
        buf.Length = totalSize;

        return new MultiplexedData(buf, -2);
    }

    /// <summary>
    /// Registers a server-initiated stream that needs stream-type identification.
    /// Only call for streams NOT opened by us — i.e. streams from the QUIC accept loop.
    /// </summary>
    public void OnServerStreamOpened(long quicStreamId)
    {
        if (quicStreamId < 0 || (quicStreamId & 1) == 0)
        {
            return;
        }

        _pendingStreamType.Add(quicStreamId);
    }

    /// <summary>
    /// Resolves a QUIC stream ID to its logical stream ID (-2 control, -3 QPACK encoder, -4 QPACK decoder).
    /// For server-initiated streams, strips the stream-type prefix from the first buffer and establishes the mapping.
    /// Returns the logical stream ID and (potentially trimmed) buffer. The caller must use the returned values.
    /// </summary>
    public (long LogicalStreamId, TransportBuffer Buffer) ResolveStreamId(long quicStreamId, TransportBuffer buffer)
    {
        if (_pendingStreamType.Remove(quicStreamId))
        {
            var span = buffer.Span;
            if (!QuicVarInt.TryDecode(span, out var rawType, out var typeBytes))
            {
                return (quicStreamId, buffer);
            }

            if ((StreamType)rawType == StreamType.Push)
            {
                HandleIncomingPushStream(quicStreamId, span[typeBytes..]);
                buffer.Dispose();
                return (quicStreamId, null!);
            }

            var logicalId = (StreamType)rawType switch
            {
                StreamType.Control => -2L,
                StreamType.QpackEncoder => -3L,
                StreamType.QpackDecoder => -4L,
                _ => quicStreamId
            };

            if (logicalId is -2 or -3 or -4)
            {
                if (!_assignedCriticalStreams.Add(logicalId))
                {
                    throw new Http3Exception(Http3ErrorCode.ClosedCriticalStream,
                        string.Concat("RFC 9114 §6.2.1: Duplicate stream type ", ((StreamType)rawType).ToString()));
                }
            }

            _serverStreamMap[quicStreamId] = logicalId;

            var remaining = span.Length - typeBytes;
            if (remaining <= 0)
            {
                buffer.Dispose();
                return (logicalId, null!);
            }

            var trimmed = TransportBuffer.Rent(remaining);
            span[typeBytes..].CopyTo(trimmed.FullMemory.Span);
            trimmed.Length = remaining;
            buffer.Dispose();
            return (logicalId, trimmed);
        }

        if (_serverStreamMap.TryGetValue(quicStreamId, out var mapped))
        {
            return (mapped, buffer);
        }

        return (quicStreamId, buffer);
    }

    /// <summary>
    /// Decodes a TransportBuffer into HTTP/3 frames using a per-stream decoder.
    /// </summary>
    public IReadOnlyList<Http3Frame> DecodeServerData(TransportBuffer buffer, long streamId)
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
        _serverStreamMap.Clear();
        _pendingStreamType.Clear();
        _assignedCriticalStreams.Clear();

        if (_transportOptions is not null)
        {
            _ops.OnOutbound(new OpenStream(-2, StreamDirection.Unidirectional));
            _ops.OnOutbound(new OpenStream(-3, StreamDirection.Unidirectional));
            _ops.OnOutbound(new OpenStream(-4, StreamDirection.Unidirectional));
            _ops.OnOutbound(new ConnectTransport(_transportOptions));
        }
    }

    /// <summary>
    /// Called when a new QUIC connection is established after a loss.
    /// </summary>
    public void OnConnectionRestored()
    {
        var wasReconnecting = IsReconnecting;
        IsReconnecting = false;
        _reconnectAttempts = 0;

        var preface = TryBuildControlPreface();
        if (preface is not null)
        {
            _ops.OnOutbound(preface);
        }

        if (wasReconnecting)
        {
            ReplayBufferedFrames();
        }
    }

    /// <summary>
    /// Called when a reconnect attempt fails.
    /// Returns true if another attempt should be made, false if max exceeded.
    /// </summary>
    public bool OnReconnectAttemptFailed()
    {
        if (_reconnectAttempts >= _options.Http3.MaxReconnectAttempts)
        {
            _ops.OnFail(new HttpRequestException("TurboHTTP: HTTP/3 reconnect failed after max attempts."));
            return false;
        }

        _reconnectAttempts++;
        return true;
    }

    /// <summary>
    /// Emits an outbound item, or buffers it if transport isn't connected yet.
    /// ConnectTransport items always emit immediately.
    /// </summary>
    private void EmitOutbound(ITransportOutbound item)
    {
        if (item is ConnectTransport || _transportConnected)
        {
            _ops.OnOutbound(item);
            return;
        }

        _preConnectBuffer.Add(item);
    }

    /// <summary>
    /// Flushes all pre-connect buffered items and clears the buffer.
    /// </summary>
    private void FlushPreConnectBuffer()
    {
        for (var i = 0; i < _preConnectBuffer.Count; i++)
        {
            _ops.OnOutbound(_preConnectBuffer[i]);
        }

        _preConnectBuffer.Clear();
    }

    /// <summary>
    /// Schedules an idle timeout check timer.
    /// </summary>
    private void ScheduleIdleCheck()
    {
        if (IsTimeoutDisabled)
        {
            return;
        }

        var remaining = TimeUntilExpiry();
        var checkInterval = remaining > TimeSpan.Zero ? remaining : TimeSpan.FromSeconds(1);
        _ops.OnScheduleTimer("idle-timeout-check", checkInterval);
    }

    /// <summary>
    /// Disposes owned resources.
    /// </summary>
    public void Dispose()
    {
        _streamManager.Dispose();

        foreach (var item in _preConnectBuffer)
        {
            if (item is TransportData { Buffer: var buffer })
            {
                buffer.Dispose();
            }
        }

        _preConnectBuffer.Clear();
    }

    private bool BufferForReconnect(HttpRequestMessage request)
    {
        if (_reconnectBuffer.Count >= _options.Http3.MaxReconnectBufferSize)
        {
            return false;
        }

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
            _transportOptions = OptionsFactory.Build(Endpoint, _options);
            _ops.OnOutbound(new ConnectTransport(_transportOptions));
        }

        var streamId = Tracker.AllocateStreamId();
        Tracker.OnStreamOpened(streamId);
        Connection.OnStreamOpened();

        _streamManager.Correlate(streamId, request);

        _ops.OnOutbound(new OpenStream(streamId, StreamDirection.Bidirectional));

        FlushEncoderInstructions();

        foreach (var f in encoded)
        {
            EmitSerializedFrame(f, streamId);
        }

        _ops.OnOutbound(new CompleteWrites(streamId));
        return true;
    }

    private IReadOnlyList<Http3Frame> EncodeToFrames(HttpRequestMessage request)
    {
        OriginValidator.Validate(request.RequestUri!, request.Method == HttpMethod.Connect);
        var frames = _requestEncoder.Encode(request);

        return frames;
    }

    private void ReplayBufferedFrames()
    {
        var oldCorrelations = _streamManager.SnapshotAndClearCorrelations();
        var replayArray = ArrayPool<Http3Frame>.Shared.Rent(_reconnectBuffer.Count);
        var replayCount = _reconnectBuffer.Count;
        _reconnectBuffer.CopyTo(replayArray);
        _reconnectBuffer.Clear();

        var correlationIndex = 0;
        long currentReplayStreamId = -1;

        for (var i = 0; i < replayCount; i++)
        {
            var frame = replayArray[i];
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

        ArrayPool<Http3Frame>.Shared.Return(replayArray, true);

        for (var i = correlationIndex; i < oldCorrelations.Count; i++)
        {
            EncodeAndEmit(oldCorrelations[i]);
        }
    }

    private void EmitSerializedFrame(Http3Frame frame, long streamId = -1)
    {
        var buf = TransportBuffer.Rent(frame.SerializedSize);
        var span = buf.FullMemory.Span;
        frame.WriteTo(ref span);
        buf.Length = frame.SerializedSize;

        if (streamId >= 0)
        {
            _ops.OnOutbound(new MultiplexedData(buf, streamId));
        }
        else
        {
            _ops.OnOutbound(new TransportData(buf));
        }
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

            var remoteSettings = Connection.RemoteSettings!;

            var peerQpackCapacity = remoteSettings.QpackMaxTableCapacity;
            if (peerQpackCapacity > 0)
            {
                TableSync.UpdateEncoderCapacity((int)peerQpackCapacity);
                FlushEncoderInstructions();
            }

            TableSync.RemoteMaxFieldSectionSize = remoteSettings.MaxFieldSectionSize;
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
        var cancelFrame = new Http3CancelPushFrame(pushPromise.PushId);
        EmitSerializedFrame(cancelFrame);
        _ops.OnWarning(
            string.Concat("RFC 9114 §7.2.5 — push promise rejected (pushId=",
                pushPromise.PushId.ToString(), "); server push not supported"));
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
            var cancel = new Http3CancelPushFrame(pushId);
            EmitSerializedFrame(cancel);
        }

        _ops.OnOutbound(new ResetStream(quicStreamId));
        _ops.OnWarning(
            string.Concat("RFC 9114 §4.6 — push stream ", quicStreamId.ToString(),
                " (pushId=", pushId.ToString(), ") reset (push response delivery not implemented)"));
    }

    private void HandleTaggedStreamData(MultiplexedData multiplexed)
    {
        var (streamId, buffer) = ResolveStreamId(multiplexed.StreamId, multiplexed.Buffer);

        if (buffer is null)
        {
            return;
        }

        switch (streamId)
        {
            case -4:
            {
                ProcessQpackDecoderBytes(buffer.Memory);
                buffer.Dispose();
                return;
            }
            case -3:
            {
                ProcessQpackEncoderBytes(buffer.Memory);
                buffer.Dispose();
                return;
            }
            case -2:
            {
                ProcessFrameData(buffer, -2);
                return;
            }
            default:
            {
                ProcessFrameData(buffer, streamId);
                return;
            }
        }
    }

    private void ProcessFrameData(TransportBuffer buffer, long streamId)
    {
        var frames = DecodeServerData(buffer, streamId);

        for (var i = 0; i < frames.Count; i++)
        {
            var frame = frames[i];
            var forwarded = ProcessFrame(frame);
            if (forwarded is not null)
            {
                AssembleResponse(forwarded, streamId);
            }
        }
    }

    // --- IHttpStateMachine explicit implementation ---

    void IHttpStateMachine.PreStart()
    {
        EmitOutbound(new OpenStream(-2, StreamDirection.Unidirectional));
        EmitOutbound(new OpenStream(-3, StreamDirection.Unidirectional));
        EmitOutbound(new OpenStream(-4, StreamDirection.Unidirectional));

        var preface = TryBuildControlPreface();
        if (preface is not null)
        {
            EmitOutbound(preface);
        }

        ScheduleIdleCheck();
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
                _transportConnected = true;
                OnConnectionRestored();
                FlushPreConnectBuffer();
                return;
            }

            case TransportDisconnected when IsReconnecting:
            {
                if (OnReconnectAttemptFailed())
                {
                    _ops.OnFail(new HttpRequestException("TurboHTTP: HTTP/3 reconnect failed after max attempts."));
                }
                return;
            }

            case TransportDisconnected when HasInFlightRequests:
            {
                OnConnectionLost();
                return;
            }

            case TransportDisconnected:
            {
                _ops.OnComplete();
                return;
            }

            case ServerStreamAccepted { StreamId: var id }:
            {
                OnServerStreamOpened(id);
                return;
            }

            case StreamOpened:
            {
                return;
            }

            case StreamReadCompleted { StreamId: >= 0 } readCompleted:
            {
                FlushPendingResponse(readCompleted.StreamId);
                return;
            }

            case StreamReadCompleted:
            {
                return;
            }

            case StreamClosed { StreamId: >= 0 } streamClosed:
            {
                if (streamClosed.Reason == DisconnectReason.Error)
                {
                    FailInflightRequest(streamClosed.StreamId,
                        new HttpRequestException("HTTP/3 stream aborted by transport."));
                }
                else
                {
                    FlushPendingResponse(streamClosed.StreamId);
                }
                return;
            }

            case StreamClosed:
            {
                FlushPendingResponse();
                return;
            }

            case MultiplexedData multiplexed:
            {
                HandleTaggedStreamData(multiplexed);
                return;
            }

            case TransportData rawData:
            {
                _ops.OnWarning("Received untagged TransportData — dropping to prevent stream ID misrouting.");
                rawData.Buffer.Dispose();
                return;
            }
        }
    }

    void IHttpStateMachine.OnUpstreamFinished()
    {
        FlushPendingResponse();

        if (IsReconnecting)
        {
            _ops.OnWarning("HTTP/3 transport closed during reconnect — discarding in-flight request(s).");
            _ops.OnComplete();
            return;
        }

        _ops.OnComplete();
    }

    void IHttpStateMachine.OnTimerFired(string name)
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
            _ops.OnOutbound(new MultiplexedData(buf, -2));
            _ops.OnComplete();
            return;
        }

        ScheduleIdleCheck();
    }

    void IHttpStateMachine.Cleanup()
    {
        Dispose();
    }
}