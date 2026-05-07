using System.Buffers;
using Servus.Akka.Transport;
using TurboHTTP.Internal;
using TurboHTTP.Protocol.Http3.Qpack;
using TurboHTTP.Streams.Stages;
using static Servus.Core.Servus;

namespace TurboHTTP.Protocol.Http3;

internal sealed class StateMachine : IHttpStateMachine
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

    // Transport connection tracking and pre-connect buffering.
    // QUIC requires ConnectTransport before OpenStream/data can be processed,
    // so we buffer outbound items until TransportConnected arrives.
    private bool _transportConnected;
    private readonly List<ITransportOutbound> _preConnectBuffer = [];

    private readonly ServerStreamResolver _serverStreamResolver;

    /// <summary>Whether a new request can be accepted (no GOAWAY + not reconnecting + concurrency budget).</summary>
    public bool CanAcceptRequest => !Connection.GoAwayReceived && !IsReconnecting && Tracker.CanOpenStream();

    /// <summary>Whether the connection is currently in the reconnection phase.</summary>
    public bool IsReconnecting { get; private set; }

    /// <summary>Number of frames buffered for replay on reconnection.</summary>
    public int ReconnectBufferCount => _reconnectBuffer.Count;

    /// <summary>Whether there are in-flight requests awaiting responses.</summary>
    public bool HasInFlightRequests => _streamManager.HasInFlightRequests;

    /// <summary>The current connection endpoint.</summary>
    public RequestEndpoint Endpoint { get; private set; }

    /// <summary>The underlying stream tracker for stream ID allocation and concurrency.</summary>
    private StreamTracker Tracker { get; }

    /// <summary>The underlying connection state for idle timeout and settings inspection.</summary>
    private ConnectionState Connection { get; }

    /// <summary>The QPACK table synchronization coordinator.</summary>
    private QpackTableSync TableSync { get; }

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
            FlushDecoderInstructionsCallback = _ => _qpackHandler.FlushDecoderInstructions(),
            OnStreamClosedCallback = OnStreamClosed
        };
        _serverStreamResolver = new ServerStreamResolver
        {
            OnPushStreamDetected = HandleIncomingPushStream
        };
        Tracker = new StreamTracker(maxConcurrentStreams: options.Http3.MaxConcurrentStreams);

        var idleTimeout = options.Http3.IdleTimeout == TimeSpan.Zero
            ? DefaultIdleTimeout
            : options.Http3.IdleTimeout;

        Connection = new ConnectionState(idleTimeout);
    }

    public void PreStart()
    {
        EmitOutbound(new OpenStream(CriticalStreamId.Control, StreamDirection.Unidirectional));
        EmitOutbound(new OpenStream(CriticalStreamId.QpackEncoder, StreamDirection.Unidirectional));
        EmitOutbound(new OpenStream(CriticalStreamId.QpackDecoder, StreamDirection.Unidirectional));

        var preface = TryBuildControlPreface();
        if (preface is not null)
        {
            EmitOutbound(preface);
        }

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

        EncodeAndEmit(request);
    }

    public void DecodeServerData(ITransportInbound data)
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
                return;
            }

            case ServerStreamAccepted { Id: var id }:
            {
                _serverStreamResolver.OnServerStreamOpened(id);
                return;
            }

            case StreamOpened:
            {
                return;
            }

            case StreamReadCompleted readCompleted when readCompleted.Id.Value >= 0:
            {
                FlushPendingResponse(readCompleted.Id.Value);
                return;
            }

            case StreamReadCompleted:
            {
                return;
            }

            case StreamClosed streamClosed when streamClosed.Id.Value >= 0:
            {
                if (streamClosed.Reason == DisconnectReason.Error)
                {
                    _streamManager.FailInflightRequest(streamClosed.Id.Value,
                        new HttpRequestException("HTTP/3 stream aborted by transport."));
                }
                else
                {
                    FlushPendingResponse(streamClosed.Id.Value);
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
                Tracing.For("Protocol").Warning(this,
                    "Received untagged TransportData — dropping to prevent stream ID misrouting.");
                rawData.Buffer.Dispose();
                return;
            }
        }
    }

    public void OnUpstreamFinished()
    {
        FlushPendingResponse();

        if (IsReconnecting)
        {
            Tracing.For("Protocol").Debug(this,
                "HTTP/3 transport closed during reconnect — discarding in-flight request(s).");
            var correlations = _streamManager.SnapshotAndClearCorrelations();
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

    public void Cleanup()
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

    private MultiplexedData? TryBuildControlPreface()
    {
        if (_controlPrefaceSent)
        {
            return null;
        }

        _controlPrefaceSent = true;

        var settings = new Settings();
        settings.Set(SettingsIdentifier.QpackMaxTableCapacity, _options.Http3.QpackMaxTableCapacity);
        settings.Set(SettingsIdentifier.QpackBlockedStreams, _options.Http3.QpackBlockedStreams);
        settings.Set(SettingsIdentifier.MaxFieldSectionSize, _options.Http3.MaxFieldSectionSize);
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

        return new MultiplexedData(buf, CriticalStreamId.Control);
    }


    public IReadOnlyList<Http3Frame> DecodeServerData(TransportBuffer buffer, long streamId)
    {
        return _streamManager.DecodeServerData(buffer, streamId);
    }

    public Http3Frame? ProcessFrame(Http3Frame frame)
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
        _qpackHandler.FlushDecoderInstructions();
        _streamManager.ResolveBlockedStreams(resolved);
    }

    private GoAwayFrame? CheckIdleTimeout()
    {
        if (Connection.IsIdleTimeoutExpired() && Connection.ActiveStreamCount == 0)
        {
            Tracing.For("Protocol").Info(this,
                "RFC 9114 §5.1 — idle timeout expired with no active streams; sending GOAWAY.");
            return new GoAwayFrame(0);
        }

        return null;
    }

    private void OnConnectionLost()
    {
        IsReconnecting = true;
        _transportConnected = false;
        _reconnectAttempts = 1;

        _streamManager.DrainStreams();

        Tracker.Reset();
        Connection.Reset();
        _streamManager.ResetAllDecoders();
        _controlPrefaceSent = false;
        _qpackHandler.Reset();
        TableSync.Reset();
        _serverStreamResolver.Reset();

        if (_transportOptions is not null)
        {
            EmitOutbound(new OpenStream(CriticalStreamId.Control, StreamDirection.Unidirectional));
            EmitOutbound(new OpenStream(CriticalStreamId.QpackEncoder, StreamDirection.Unidirectional));
            EmitOutbound(new OpenStream(CriticalStreamId.QpackDecoder, StreamDirection.Unidirectional));
            EmitOutbound(new ConnectTransport(_transportOptions));
        }
    }

    private void OnConnectionRestored()
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

    private void OnReconnectAttemptFailed()
    {
        if (_reconnectAttempts >= _options.Http3.MaxReconnectAttempts)
        {
            Tracing.For("Protocol").Info(this, "HTTP/3 reconnect failed after {0} attempts", _reconnectAttempts);
            var correlations = _streamManager.SnapshotAndClearCorrelations();
            if (correlations.Count > 0)
            {
                var exception = new HttpRequestException("HTTP/3 reconnect failed after max attempts.");
                RequestFault.FailAll(correlations, exception);
            }

            return;
        }

        _reconnectAttempts++;
    }

    private void EmitOutbound(ITransportOutbound item)
    {
        if (item is ConnectTransport || _transportConnected)
        {
            _ops.OnOutbound(item);
            return;
        }

        _preConnectBuffer.Add(item);
    }

    private void FlushPreConnectBuffer()
    {
        for (var i = 0; i < _preConnectBuffer.Count; i++)
        {
            _ops.OnOutbound(_preConnectBuffer[i]);
        }

        _preConnectBuffer.Clear();
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
        if (_reconnectBuffer.Count >= _options.Http3.MaxReconnectBufferSize)
        {
            return;
        }

        var frames = EncodeToFrames(request);
        foreach (var f in frames)
        {
            _reconnectBuffer.Add(f);
        }

        var reconnectStreamId = Tracker.AllocateStreamId();
        _streamManager.Correlate(reconnectStreamId, request);
    }

    private void EncodeAndEmit(HttpRequestMessage request)
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

        var streamTarget = StreamTarget.FromId(streamId);
        EmitOutbound(new OpenStream(streamTarget, StreamDirection.Bidirectional));

        _qpackHandler.FlushEncoderInstructions();;

        foreach (var f in encoded)
        {
            EmitSerializedFrame(f, streamId);
        }

        EmitOutbound(new CompleteWrites(streamTarget));
    }

    private IReadOnlyList<Http3Frame> EncodeToFrames(HttpRequestMessage request)
    {
        OriginValidator.Validate(request.RequestUri!, request.Method == HttpMethod.Connect);
        return _requestEncoder.Encode(request);
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
            if (frame is HeadersFrame)
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
            EmitOutbound(new MultiplexedData(buf, streamId));
        }
        else
        {
            EmitOutbound(new TransportData(buf));
        }
    }

    private void OnStreamClosed(long streamId)
    {
        Tracker.OnStreamClosed(streamId);
        Connection.OnStreamClosed();
    }

    private void HandleSettings(SettingsFrame settings)
    {
        try
        {
            Connection.OnRemoteSettings(settings);
            Tracing.For("Protocol").Info(this, "RFC 9114 §7.2.4 — remote SETTINGS received ({0} parameters).",
                settings.Parameters.Count);

            var remoteSettings = Connection.RemoteSettings!;

            var peerQpackCapacity = remoteSettings.QpackMaxTableCapacity;
            if (peerQpackCapacity > 0)
            {
                TableSync.UpdateEncoderCapacity((int)peerQpackCapacity);
                _qpackHandler.FlushEncoderInstructions();
            }

            TableSync.RemoteMaxFieldSectionSize = remoteSettings.MaxFieldSectionSize;
        }
        catch (Http3Exception ex)
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
        catch (Http3Exception ex)
        {
            Tracing.For("Protocol").Warning(this, "GOAWAY error absorbed — {0}", ex.Message);
            Connection.GoAwayReceived = true;
        }
    }

    private PushPromiseFrame? HandlePushPromise(PushPromiseFrame pushPromise)
    {
        var cancelFrame = new CancelPushFrame(pushPromise.PushId);
        EmitSerializedFrame(cancelFrame);
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
            EmitSerializedFrame(cancel);
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
                ProcessQpackDecoderBytes(resolved.Buffer.Memory);
                resolved.Buffer.Dispose();
                return;
            }
            case CriticalStreamId.QpackEncoderId:
            {
                ProcessQpackEncoderBytes(resolved.Buffer.Memory);
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
}