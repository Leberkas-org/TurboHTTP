using Servus.Akka.Transport;
using TurboHTTP.Client;
using TurboHTTP.Internal;
using TurboHTTP.Protocol.Multiplexed;
using TurboHTTP.Protocol.Multiplexed.Body;
using TurboHTTP.Protocol.Semantics;
using TurboHTTP.Protocol.Syntax.Http2.Options;
using TurboHTTP.Streams.Stages.Client;
using static Servus.Core.Servus;

namespace TurboHTTP.Protocol.Syntax.Http2.Client;

internal sealed class Http2ClientSessionManager
{
    private readonly Http2ClientEncoderOptions _encoderOptions;
    private readonly Http2ClientDecoderOptions _decoderOptions;
    private readonly TurboClientOptions _options;
    private readonly IClientStageOperations _ops;

    private readonly StreamTracker _tracker;
    private readonly FlowController _flow;
    private readonly StackStreamStatePool<StreamState> _statePool;
    private readonly FrameDecoder _frameDecoder = new();
    private readonly Http2ClientDecoder _responseDecoder;
    private readonly Http2ClientEncoder _requestEncoder;
    private readonly Dictionary<int, HttpRequestMessage> _correlationMap = new();

    private readonly Dictionary<int, StreamState> _streams = new();

    private bool _prefaceSent;
    private bool _awaitingPingAck;
    private long _pingSentTimestamp;

    public bool CanOpenStream => _tracker.CanOpenStream();
    public bool GoAwayReceived => _flow.GoAwayReceived;
    public int GoAwayLastStreamId { get; private set; }
    public bool HasInFlightRequests => _correlationMap.Count > 0;
    public bool HasActiveStreams => _streams.Count > 0;
    public RequestEndpoint Endpoint { get; private set; }

    public Http2ClientSessionManager(
        Http2ClientEncoderOptions encoderOptions,
        Http2ClientDecoderOptions decoderOptions,
        TurboClientOptions options,
        IClientStageOperations ops)
    {
        _encoderOptions = encoderOptions;
        _decoderOptions = decoderOptions;
        _options = options;
        _ops = ops;
        _tracker = new StreamTracker(1, decoderOptions.MaxConcurrentStreams);
        _flow = new FlowController(
            decoderOptions.InitialConnectionWindowSize,
            decoderOptions.InitialStreamWindowSize);
        _requestEncoder = new Http2ClientEncoder(useHuffman: true, maxFrameSize: encoderOptions.MaxFrameSize);
        var poolCapacity = Math.Min(
            _tracker.MaxConcurrentStreams > 0 ? _tracker.MaxConcurrentStreams : 100,
            1000);
        _statePool = new StackStreamStatePool<StreamState>(poolCapacity, () => new StreamState());
        _responseDecoder = new Http2ClientDecoder();
        _responseDecoder.SetMaxAllowedTableSize(encoderOptions.HeaderTableSize);
    }

    public TransportData? TryBuildPreface()
    {
        if (_decoderOptions.InitialConnectionWindowSize <= 0 || _prefaceSent)
        {
            return null;
        }

        _prefaceSent = true;
        var (prefaceOwner, prefaceLength) = PrefaceBuilder.Build(
            _decoderOptions.InitialConnectionWindowSize,
            _encoderOptions.HeaderTableSize,
            _encoderOptions.MaxFrameSize);
        var prefaceBuf = TransportBuffer.Rent(prefaceLength);
        prefaceOwner.Memory.Span[..prefaceLength].CopyTo(prefaceBuf.FullMemory.Span);
        prefaceOwner.Dispose();
        prefaceBuf.Length = prefaceLength;
        return new TransportData(prefaceBuf);
    }

    public void EncodeRequest(HttpRequestMessage request)
    {
        var streamId = _tracker.AllocateStreamId();

        if (GoAwayReceived)
        {
            Tracing.For("Protocol").Warning(this,
                "HTTP/2: RFC 9113 §6.8 — GOAWAY received; dropping new request (stream {0})", streamId);
            request.Fail(new HttpRequestException("HTTP/2 GOAWAY received."));
            return;
        }

        var endpoint = request.RequestUri is not null
            ? RequestEndpoint.FromRequest(request)
            : RequestEndpoint.Default;

        if (Endpoint == default && endpoint != default)
        {
            Endpoint = endpoint;
            var transportOptions = OptionsFactory.Build(Endpoint, _options);
            _ops.OnOutbound(new ConnectTransport(transportOptions));

            var preface = TryBuildPreface();
            if (preface is not null)
            {
                _ops.OnOutbound(preface);
            }
        }

        _correlationMap.TryAdd(streamId, request);

        if (request.RequestUri is null)
        {
            _tracker.OnStreamOpened(streamId);
            return;
        }

        var frames = _requestEncoder.Encode(request, streamId);

        if (frames.Count == 0)
        {
            return;
        }

        if (frames[0] is HeadersFrame headersFrame)
        {
            _tracker.OnStreamOpened(headersFrame.StreamId);
            _flow.InitStreamSendWindow(headersFrame.StreamId);
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

        if (request.Content is null)
        {
            return;
        }

        if (!_streams.TryGetValue(streamId, out var state))
        {
            state = _statePool.Rent();
            _streams[streamId] = state;
        }

        var contentLength = request.Content?.Headers.ContentLength;
        var bodyStream = request.Content?.ReadAsStream();
        var encoder = BodyEncoderFactory.Create(bodyStream, contentLength);
        if (encoder is null)
        {
            return;
        }

        state.InitBodyEncoder(encoder);
        state.StartBodyEncoder(bodyStream!, streamId, _ops.StageActor);
    }

    public IReadOnlyList<Http2Frame> DecodeFrames(TransportBuffer buffer)
    {
        return _frameDecoder.Decode(buffer);
    }

    public void ProcessFrame(Http2Frame frame)
    {
        switch (frame)
        {
            case SettingsFrame settings:
                HandleSettings(settings);
                break;

            case DataFrame data:
                ProcessDataFrame(data);
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
                HandleWindowUpdate(win);
                break;

            case PingFrame ping:
                HandlePing(ping);
                break;

            case GoAwayFrame goAway:
                HandleGoAway(goAway);
                break;
        }
    }

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

    public bool IsKeepAliveTimedOut(TimeSpan timeout)
    {
        if (!_awaitingPingAck)
        {
            return false;
        }

        var elapsed = Environment.TickCount64 - _pingSentTimestamp;
        return elapsed >= (long)timeout.TotalMilliseconds;
    }

    public IReadOnlyDictionary<int, HttpRequestMessage> GetCorrelationMap()
    {
        return _correlationMap;
    }

    public bool HasReceivedHeaders(int streamId)
    {
        return _streams.GetValueOrDefault(streamId)?.HasResponse ?? false;
    }

    public void ReleaseAllStreamState()
    {
        foreach (var (_, state) in _streams)
        {
            state.Reset();
            _statePool.Return(state);
        }

        _streams.Clear();
        _correlationMap.Clear();
    }

    public void ResetConnectionState()
    {
        _tracker.Reset();
        _flow.Reset(_decoderOptions.InitialConnectionWindowSize, _decoderOptions.InitialStreamWindowSize);
        _requestEncoder.ResetHpack();
        _responseDecoder.ResetHpack();
        _prefaceSent = false;
    }

    public void Cleanup()
    {
        foreach (var (_, state) in _streams)
        {
            state.AbortBody();
        }

        ReleaseAllStreamState();
    }

    private void EmitDataFrames(int streamId, ReadOnlyMemory<byte> data)
    {
        var maxFrame = _encoderOptions.MaxFrameSize;
        var remaining = data;
        while (remaining.Length > maxFrame)
        {
            EmitFrame(new DataFrame(streamId, remaining[..maxFrame], endStream: false));
            remaining = remaining[maxFrame..];
        }

        if (!remaining.IsEmpty)
        {
            EmitFrame(new DataFrame(streamId, remaining, endStream: false));
        }
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
        var result = _flow.OnRemoteSettings(frame);

        if (result.AckFrame is null)
        {
            return;
        }

        if (result.MaxConcurrentStreamsChange is { } maxStreams)
        {
            _tracker.SetMaxConcurrentStreams(maxStreams);
        }

        _requestEncoder.ApplyServerSettings(frame.Parameters);
        EmitFrame(result.AckFrame);
    }

    private void ProcessDataFrame(DataFrame data)
    {
        var result = _flow.OnInboundData(data.StreamId, data.Data.Length);

        if (result.IsConnectionViolation)
        {
            Tracing.For("Protocol").Info(this,
                "HTTP/2: RFC 9113 §6.9 — connection flow control window exceeded. Triggering reconnect");
            _ops.OnOutbound(new DisconnectTransport(DisconnectReason.Error));
            return;
        }

        if (result.IsStreamViolation)
        {
            Tracing.For("Protocol").Info(this,
                "HTTP/2: RFC 9113 §6.9 — stream {0} flow control window exceeded. Triggering reconnect", data.StreamId);
            _ops.OnOutbound(new DisconnectTransport(DisconnectReason.Error));
            return;
        }

        if (result.ConnectionWindowUpdate is { } connUpdate)
        {
            EmitFrame(new WindowUpdateFrame(connUpdate.StreamId, connUpdate.Increment));
        }

        if (result.StreamWindowUpdate is { } streamUpdate)
        {
            EmitFrame(new WindowUpdateFrame(streamUpdate.StreamId, streamUpdate.Increment));
        }

        HandleData(data);

        if (data.EndStream)
        {
            var hasActiveBodyEncoder = _streams.TryGetValue(data.StreamId, out var state)
                                      && state.HasBodyEncoder
                                      && !state.IsBodyEncoderComplete;
            if (!hasActiveBodyEncoder)
            {
                CloseStream(data.StreamId);
            }
        }
    }

    private void HandlePing(PingFrame ping)
    {
        if (ping.IsAck)
        {
            _awaitingPingAck = false;
            return;
        }

        var ack = _flow.OnPing(ping);
        if (ack is not null)
        {
            EmitFrame(ack);
        }
    }

    private void HandleGoAway(GoAwayFrame goAway)
    {
        _flow.OnGoAway();
        GoAwayLastStreamId = goAway.LastStreamId;
        Tracing.For("Protocol").Info(this,
            "HTTP/2: GOAWAY received from {0} — LastStreamId={1}, ErrorCode={2}. Reconnecting", Endpoint.Host,
            goAway.LastStreamId, goAway.ErrorCode);
    }

    private void CloseStream(int streamId)
    {
        if (_streams.TryGetValue(streamId, out var state) && state.HasBodyDecoder)
        {
            state.AbortBody();
        }

        _tracker.OnStreamClosed(streamId);
        _flow.RemoveStreamSendWindow(streamId);

        var signal = _flow.OnStreamClosed(streamId);
        if (signal is { } windowUpdate)
        {
            EmitFrame(new WindowUpdateFrame(windowUpdate.StreamId, windowUpdate.Increment));
        }
    }

    private void HandleHeaders(HeadersFrame frame)
    {
        if (!_streams.TryGetValue(frame.StreamId, out var state))
        {
            state = _statePool.Rent();
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
            Tracing.For("Protocol").Warning(this, "HTTP/2: Received CONTINUATION for unknown stream {0} — dropping",
                frame.StreamId);
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
            Tracing.For("Protocol").Warning(this, "HTTP/2: Received DATA for unknown stream {0} — dropping",
                frame.StreamId);
            return;
        }

        if (!state.HasBodyDecoder)
        {
            Tracing.For("Protocol").Warning(this, "HTTP/2: Received DATA before HEADERS on stream {0} — dropping",
                frame.StreamId);
            return;
        }

        state.FeedBody(frame.Data.Span, frame.EndStream);

        if (frame.EndStream)
        {
            state.DetachBodyDecoder();
            state.MarkRemoteClosed();

            if (!state.HasBodyEncoder || state.IsBodyEncoderComplete)
            {
                _streams.Remove(frame.StreamId);
                state.Reset();
                _statePool.Return(state);
            }
        }
    }

    private void DecodeHeaders(int streamId, bool endStream)
    {
        if (!_streams.TryGetValue(streamId, out var state))
        {
            Tracing.For("Protocol").Warning(this, "HTTP/2: DecodeHeaders called for unknown stream {0} — dropping",
                streamId);
            return;
        }

        if (state.HasResponse)
        {
            _responseDecoder.DecodeTrailers(state);
            if (endStream)
            {
                _streams.Remove(streamId);
                state.DetachBodyDecoder();
                state.Reset();
                _statePool.Return(state);
            }

            return;
        }

        if (endStream)
        {
            var response = _responseDecoder.DecodeHeaders(streamId, true, state);
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
                Tracing.For("Protocol").Warning(this, "HTTP/2: {0}", partialContentResult.ErrorMessage!);
            }

            _ops.OnResponse(response);

            _streams.Remove(streamId);
            state.Reset();
            _statePool.Return(state);
            return;
        }

        var streamingResponse = _responseDecoder.DecodeHeadersForStreaming(streamId, state);
        state.InitBodyDecoder(BodyDecoderFactory.Create(streaming: true));
        var bodyStream = state.GetBodyStream();
        streamingResponse.Content = new StreamContent(bodyStream);
        state.ApplyContentHeadersTo(streamingResponse.Content);

        if (_correlationMap.Remove(streamId, out var request))
        {
            streamingResponse.RequestMessage = request;
        }

        var partialResult = PartialContentValidator.Validate(streamingResponse);
        if (!partialResult.IsValid)
        {
            Tracing.For("Protocol").Warning(this, "HTTP/2: {0}", partialResult.ErrorMessage!);
        }

        _ops.OnResponse(streamingResponse);
    }

    public void OnBodyMessage(object msg)
    {
        switch (msg)
        {
            case StreamBodyChunk<int> chunk:
                HandleOutboundBodyChunk(chunk);
                break;

            case StreamBodyComplete<int> complete:
                HandleOutboundBodyComplete(complete.StreamId);
                break;

            case StreamBodyFailed<int>(var failedStreamId, var exception):
                Tracing.For("Protocol").Warning(this,
                    "HTTP/2: Body encoding failed for stream {0}: {1}", failedStreamId, exception.Message);
                EmitFrame(new RstStreamFrame(failedStreamId, Http2ErrorCode.InternalError));
                CloseStream(failedStreamId);
                break;
        }
    }

    private void HandleOutboundBodyChunk(StreamBodyChunk<int> chunk)
    {
        var streamId = chunk.StreamId;
        if (!_streams.TryGetValue(streamId, out var state))
        {
            chunk.Owner.Dispose();
            return;
        }

        var window = _flow.GetSendWindow(streamId);
        if (window >= chunk.Length)
        {
            EmitDataFrames(streamId, chunk.Owner.Memory[..chunk.Length]);
            _flow.OnDataSent(streamId, chunk.Length);
            chunk.Owner.Dispose();
            return;
        }

        state.EnqueueBodyChunk(chunk);
    }

    private void HandleOutboundBodyComplete(int streamId)
    {
        if (!_streams.TryGetValue(streamId, out var state))
        {
            return;
        }

        state.MarkBodyEncoderComplete();

        if (!state.HasPendingOutbound)
        {
            EmitFrame(new DataFrame(streamId, ReadOnlyMemory<byte>.Empty, endStream: true));
            CloseStream(streamId);

            if (state.IsRemoteClosed)
            {
                _streams.Remove(streamId);
                state.Reset();
                _statePool.Return(state);
            }
        }
    }

    private void DrainOutboundBuffer(int streamId)
    {
        if (!_streams.TryGetValue(streamId, out var state) || !state.HasPendingOutbound)
        {
            return;
        }

        while (state.PeekBodyChunk() is { } next)
        {
            var window = _flow.GetSendWindow(streamId);
            if (window < next.Length)
            {
                break;
            }

            state.TryDequeueBodyChunk(out var chunk);
            EmitDataFrames(streamId, chunk!.Owner.Memory[..chunk.Length]);
            _flow.OnDataSent(streamId, chunk.Length);
            chunk.Owner.Dispose();
        }

        if (state is { HasPendingOutbound: false, IsBodyEncoderComplete: true })
        {
            EmitFrame(new DataFrame(streamId, ReadOnlyMemory<byte>.Empty, endStream: true));
            CloseStream(streamId);

            if (state.IsRemoteClosed)
            {
                _streams.Remove(streamId);
                state.Reset();
                _statePool.Return(state);
            }
        }
    }

    private void HandleWindowUpdate(WindowUpdateFrame frame)
    {
        _flow.OnSendWindowUpdate(frame.StreamId, frame.Increment);

        if (frame.StreamId == 0)
        {
            foreach (var streamId in _streams.Keys.ToList())
            {
                DrainOutboundBuffer(streamId);
            }
        }
        else
        {
            DrainOutboundBuffer(frame.StreamId);
        }
    }
}