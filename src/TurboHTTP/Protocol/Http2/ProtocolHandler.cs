using Servus.Akka.Transport;
using TurboHTTP.Internal;
using TurboHTTP.Protocol.Http2.Hpack;
using TurboHTTP.Protocol.Semantics;
using TurboHTTP.Streams.Stages;

namespace TurboHTTP.Protocol.Http2;

internal sealed class ProtocolHandler
{
    private const int MaxStatePoolCapacity = 1000;
    private readonly TurboClientOptions _options;
    private readonly IStageOperations _ops;

    private readonly StreamTracker _tracker;
    private readonly FlowHandler _flow;
    private readonly FrameDecoder _frameDecoder = new();
    private readonly ResponseDecoder _responseDecoder;
    private readonly RequestEncoder _requestEncoder;
    private readonly Dictionary<int, HttpRequestMessage> _correlationMap = new();

    private readonly Dictionary<int, StreamState> _streams = new();
    private readonly Stack<StreamState> _statePool;
    private int _statePoolCapacity;

    private bool _prefaceSent;
    private bool _awaitingPingAck;
    private long _pingSentTimestamp;

    public bool CanOpenStream => _tracker.CanOpenStream();
    public bool GoAwayReceived => _flow.GoAwayReceived;
    public int GoAwayLastStreamId { get; private set; }
    public bool HasInFlightRequests => _correlationMap.Count > 0;
    public RequestEndpoint Endpoint { get; private set; }

    public ProtocolHandler(TurboClientOptions options, IStageOperations ops)
    {
        _options = options;
        _ops = ops;
        _tracker = new StreamTracker(1, options.Http2.MaxConcurrentStreams);
        _flow = new FlowHandler(options.Http2.InitialConnectionWindowSize,
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

    public void EncodeRequest(HttpRequestMessage request)
    {
        var streamId = _tracker.AllocateStreamId();

        if (GoAwayReceived)
        {
            _ops.OnWarning(
                $"RFC 9113 §6.8 — GOAWAY received; dropping new request (stream {streamId}).");
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
                _flow.OnWindowUpdate(win, _requestEncoder);
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
            ReturnState(state);
        }

        _streams.Clear();
        _correlationMap.Clear();
    }

    public void ResetConnectionState()
    {
        _tracker.Reset();
        _flow.Reset(_options.Http2.InitialConnectionWindowSize, _options.Http2.InitialStreamWindowSize);
        _requestEncoder.ResetHpack();
        _responseDecoder.ResetHpack();
        _prefaceSent = false;
    }

    public void Cleanup()
    {
        ReleaseAllStreamState();
        _statePool.Clear();
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
            _tracker.MaxConcurrentStreams = maxStreams;
            _statePoolCapacity = Math.Min(
                _tracker.MaxConcurrentStreams > 0 ? _tracker.MaxConcurrentStreams : 100,
                MaxStatePoolCapacity);
        }

        _requestEncoder.ApplyServerSettings(frame.Parameters);
        EmitFrame(result.AckFrame);
    }

    private void ProcessDataFrame(DataFrame data)
    {
        var result = _flow.OnInboundData(data.StreamId, data.Data.Length);

        if (result.IsConnectionViolation)
        {
            _ops.OnWarning("RFC 9113 §6.9 — connection flow control window exceeded. Triggering reconnect.");
            _ops.OnOutbound(new DisconnectTransport(DisconnectReason.Error));
            return;
        }

        if (result.IsStreamViolation)
        {
            _ops.OnWarning(
                $"RFC 9113 §6.9 — stream {data.StreamId} flow control window exceeded. Triggering reconnect.");
            _ops.OnOutbound(new DisconnectTransport(DisconnectReason.Error));
            return;
        }

        if (result.ConnectionWindowUpdate is { } connUpdate)
        {
            EmitFrame(connUpdate);
        }

        if (result.StreamWindowUpdate is { } streamUpdate)
        {
            EmitFrame(streamUpdate);
        }

        HandleData(data);

        if (data.EndStream)
        {
            CloseStream(data.StreamId);
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
        _ops.OnWarning(
            $"TurboHTTP: GOAWAY received from {Endpoint.Host} — LastStreamId={goAway.LastStreamId}, ErrorCode={goAway.ErrorCode}. Reconnecting.");
    }

    private void CloseStream(int streamId)
    {
        _tracker.OnStreamClosed(streamId);

        var windowUpdate = _flow.OnStreamClosed(streamId);
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

        _ops.OnResponse(response);

        _streams.Remove(streamId);
        ReturnState(state);
    }
}
