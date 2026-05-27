using System.Buffers;
using Servus.Akka.Transport;
using TurboHTTP.Client;
using TurboHTTP.Internal;
using TurboHTTP.Protocol.Multiplexed;
using TurboHTTP.Protocol.Multiplexed.Body;
using TurboHTTP.Protocol.Syntax.Http3.Options;
using TurboHTTP.Protocol.Syntax.Http3.Qpack;
using TurboHTTP.Streams.Stages.Client;
using static Servus.Core.Servus;

namespace TurboHTTP.Protocol.Syntax.Http3.Client;

internal sealed class Http3ClientSessionManager
{
    private readonly Http3ClientEncoderOptions _encoderOptions;
    private readonly Http3ClientDecoderOptions _decoderOptions;
    private readonly TurboClientOptions _options;
    private readonly IClientStageOperations _ops;

    private readonly QuicStreamTracker _tracker;
    private readonly QpackStreamManager _qpackStreamManager;
    private readonly StreamManager _streamManager;

    private readonly Http3ClientEncoder _requestEncoder;
    private readonly Http3ClientDecoder _responseDecoder;
    private readonly QpackTableSync _tableSync;

    private readonly Dictionary<long, HttpRequestMessage> _correlationMap = new();

    private bool _controlPrefaceSent;
    private bool _transportConnected;
    private readonly List<ITransportOutbound> _preConnectBuffer = [];

    public bool CanOpenStream => _tracker.CanOpenStream();
    public bool GoAwayReceived { get; private set; }
    public bool HasInFlightRequests => _correlationMap.Count > 0 || _streamManager.HasInFlightRequests;
    public RequestEndpoint Endpoint { get; private set; }

    public Http3ClientSessionManager(
        Http3ClientEncoderOptions encoderOptions,
        Http3ClientDecoderOptions decoderOptions,
        TurboClientOptions options,
        IClientStageOperations ops)
    {
        _encoderOptions = encoderOptions;
        _decoderOptions = decoderOptions;
        _options = options;
        _ops = ops;

        _tracker = new QuicStreamTracker(initialNextStreamId: 0, decoderOptions.MaxConcurrentStreams);

        _tableSync = new QpackTableSync(
            encoderMaxCapacity: 0,
            decoderMaxCapacity: encoderOptions.QpackMaxTableCapacity,
            maxBlockedStreams: encoderOptions.QpackBlockedStreams,
            configuredEncoderLimit: encoderOptions.QpackMaxTableCapacity);

        _requestEncoder = new Http3ClientEncoder(_tableSync);
        _responseDecoder = new Http3ClientDecoder(_tableSync, decoderOptions.MaxFieldSectionSize);
        _qpackStreamManager = new QpackStreamManager(ops, _requestEncoder, _responseDecoder, _tableSync);
        _streamManager = new StreamManager(ops, _responseDecoder, _tableSync)
        {
            OnStreamClosedCallback = OnStreamClosed
        };
    }

    private void OnStreamClosed(long streamId)
    {
        _correlationMap.Remove(streamId);
    }

    public void EncodeRequest(HttpRequestMessage request)
    {
        var endpoint = request.RequestUri is not null
            ? RequestEndpoint.FromRequest(request)
            : RequestEndpoint.Default;

        if (Endpoint == default && endpoint != default)
        {
            Endpoint = endpoint;
            var transportOptions = OptionsFactory.Build(Endpoint, _options);
            _ops.OnOutbound(new ConnectTransport(transportOptions));

            var preface = TryBuildControlPreface();
            if (preface is not null)
            {
                _ops.OnOutbound(preface);
            }
        }

        var streamId = _tracker.AllocateStreamId();
        _tracker.OnStreamOpened(streamId);

        EmitOutbound(new OpenStream(StreamTarget.FromId(streamId), StreamDirection.Bidirectional));

        _correlationMap.TryAdd(streamId, request);
        _streamManager.Correlate(streamId, request);

        if (request.RequestUri is null)
        {
            return;
        }

        var frames = _requestEncoder.Encode(request);
        if (frames.Count == 0)
        {
            return;
        }

        _qpackStreamManager.AccumulateEncoderInstructions();
        _qpackStreamManager.FlushIfNeeded();

        foreach (var frame in frames)
        {
            EmitSerializedFrame(frame, streamId);
        }

        if (request.Content is null)
        {
            EmitOutbound(new CompleteWrites(StreamTarget.FromId(streamId)));
            return;
        }

        var contentLength = request.Content?.Headers.ContentLength;
        var bodyStream = request.Content?.ReadAsStream();
        var encoder = BodyEncoderFactory.Create(bodyStream, contentLength);
        if (encoder is null)
        {
            EmitOutbound(new CompleteWrites(StreamTarget.FromId(streamId)));
            return;
        }

        var state = _streamManager.GetOrCreateStreamState(streamId);
        state.InitBodyEncoder(encoder);
        state.StartBodyEncoder(bodyStream!, streamId, _ops.StageActor);
    }

    public void OnBodyMessage(object msg)
    {
        switch (msg)
        {
            case StreamBodyChunk<long> chunk:
                HandleOutboundBodyChunk(chunk);
                break;

            case StreamBodyComplete<long> complete:
                EmitOutbound(new CompleteWrites(StreamTarget.FromId(complete.StreamId)));
                break;

            case StreamBodyFailed<long> failed:
                Tracing.For("Protocol").Warning(this,
                    "HTTP/3: Body encoding failed for stream {0}: {1}", failed.StreamId, failed.Reason.Message);
                EmitOutbound(new ResetStream(failed.StreamId));
                break;
        }
    }

    private void HandleOutboundBodyChunk(StreamBodyChunk<long> chunk)
    {
        var dataFrame = new DataFrame(chunk.Owner.Memory[..chunk.Length]);
        EmitSerializedFrame(dataFrame, chunk.StreamId);
        chunk.Owner.Dispose();
    }

    public void OpenCriticalStreams()
    {
        _qpackStreamManager.OpenCriticalStreams(EmitOutbound);
    }

    public MultiplexedData? TryBuildControlPreface()
    {
        if (_controlPrefaceSent)
        {
            return null;
        }

        _controlPrefaceSent = true;

        var settings = new Settings();
        settings.Set(SettingsIdentifier.QpackMaxTableCapacity, _encoderOptions.QpackMaxTableCapacity);
        settings.Set(SettingsIdentifier.QpackBlockedStreams, _encoderOptions.QpackBlockedStreams);
        settings.Set(SettingsIdentifier.MaxFieldSectionSize, _decoderOptions.MaxFieldSectionSize);
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

    public void AssembleResponse(Http3Frame frame, long streamId)
    {
        _streamManager.AssembleResponse(frame, streamId, Endpoint);
    }

    public void FlushPendingResponse(long streamId)
    {
        _streamManager.FlushPendingResponse(streamId);
    }

    public void FlushAllPendingResponses()
    {
        _streamManager.FlushAllPendingResponses();
    }

    public void ProcessQpackDecoderBytes(ReadOnlyMemory<byte> data)
    {
        _qpackStreamManager.ProcessDecoderInstructions(data.Span);
    }

    public void ProcessQpackEncoderBytes(ReadOnlyMemory<byte> data)
    {
        var resolved = _qpackStreamManager.ProcessEncoderInstructionsAndResolveBlocked(data.Span);
        _qpackStreamManager.FlushDecoderInstructions();
        _streamManager.ResolveBlockedStreams(resolved);
    }

    public void HandleSettings(SettingsFrame settings)
    {
        var remoteSettings = new Settings();
        foreach (var (id, val) in settings.Parameters)
        {
            remoteSettings.Set(id, val);
        }

        _qpackStreamManager.ApplyPeerSettings(remoteSettings);
    }

    public void OnTransportConnected()
    {
        _transportConnected = true;
        FlushPreConnectBuffer();
    }

    public void OnTransportDisconnected()
    {
        _transportConnected = false;
    }

    public IReadOnlyDictionary<long, HttpRequestMessage> GetCorrelationMap()
    {
        return _correlationMap;
    }

    public List<HttpRequestMessage> SnapshotAndClearCorrelations()
    {
        var snapshot = _correlationMap.Values.ToList();
        _correlationMap.Clear();
        return snapshot;
    }

    public void ResetConnectionState()
    {
        _tracker.Reset();
        _controlPrefaceSent = false;
        _tableSync.Reset();
        _qpackStreamManager.Reset();
        _streamManager.ResetAllDecoders();
    }

    public void Cleanup()
    {
        var exception = new HttpRequestException("HTTP/3 connection closed while requests were in flight.");
        foreach (var (_, request) in _correlationMap)
        {
            request.Fail(exception);
        }

        _correlationMap.Clear();
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

    public void DrainStreams()
    {
        _streamManager.DrainStreams();
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

    private void EmitSerializedFrame(Http3Frame frame, long streamId)
    {
        var buf = TransportBuffer.Rent(frame.SerializedSize);
        var span = buf.FullMemory.Span;
        frame.WriteTo(ref span);
        buf.Length = frame.SerializedSize;

        EmitOutbound(new MultiplexedData(buf, streamId));
    }
}