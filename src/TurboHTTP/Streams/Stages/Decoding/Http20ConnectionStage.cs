using System.Buffers;
using System.Collections.Immutable;
using System.Net;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHTTP.Internal;
using TurboHTTP.Protocol.Http2.Hpack;
using TurboHTTP.Protocol.Http11;
using TurboHTTP.Protocol.Http2;

namespace TurboHTTP.Streams.Stages.Decoding;

public sealed class Http20ConnectionShape : Shape
{
    public Inlet<Http2Frame> InServer { get; }
    public Outlet<HttpResponseMessage> OutResponse { get; }
    public Inlet<HttpRequestMessage> InApp { get; }
    public Outlet<Http2Frame> OutServer { get; }
    public Outlet<IControlItem> OutSignal { get; }

    public Http20ConnectionShape(
        Inlet<Http2Frame> inServer,
        Outlet<HttpResponseMessage> outResponse,
        Inlet<HttpRequestMessage> inApp,
        Outlet<Http2Frame> outServer,
        Outlet<IControlItem> outSignal)
    {
        InServer = inServer;
        OutResponse = outResponse;
        InApp = inApp;
        OutServer = outServer;
        OutSignal = outSignal;
    }

    public override ImmutableArray<Inlet> Inlets =>
        [InServer, InApp];

    public override ImmutableArray<Outlet> Outlets =>
        [OutResponse, OutServer, OutSignal];

    public override Shape DeepCopy()
    {
        return new Http20ConnectionShape(
            (Inlet<Http2Frame>)InServer.CarbonCopy(),
            (Outlet<HttpResponseMessage>)OutResponse.CarbonCopy(),
            (Inlet<HttpRequestMessage>)InApp.CarbonCopy(),
            (Outlet<Http2Frame>)OutServer.CarbonCopy(),
            (Outlet<IControlItem>)OutSignal.CarbonCopy());
    }

    public override Shape CopyFromPorts(ImmutableArray<Inlet> inlets, ImmutableArray<Outlet> outlets)
    {
        return new Http20ConnectionShape(
            (Inlet<Http2Frame>)inlets[0],
            (Outlet<HttpResponseMessage>)outlets[0],
            (Inlet<HttpRequestMessage>)inlets[1],
            (Outlet<Http2Frame>)outlets[1],
            (Outlet<IControlItem>)outlets[2]);
    }
}

public sealed class Http20ConnectionStage : GraphStage<Http20ConnectionShape>
{
    private const int DefaultMaxHeaderSize = 16 * 1024; // 16 KB per header field
    private const int DefaultMaxTotalHeaderSize = 64 * 1024; // 64 KB total headers

    private readonly Inlet<Http2Frame> _inServer = new("Http20Connection.In.Server");
    private readonly Outlet<HttpResponseMessage> _outResponse = new("Http20Connection.Out.Response");
    private readonly Inlet<HttpRequestMessage> _inApp = new("Http20Connection.In.App");
    private readonly Outlet<Http2Frame> _outServer = new("Http20Connection.Out.Server");
    private readonly Outlet<IControlItem> _outSignal = new("Http20Connection.Out.Signal");

    private readonly int _initialRecvWindowSize;
    private readonly int _maxConcurrentStreams;
    private readonly int _maxHeaderSize;
    private readonly int _maxTotalHeaderSize;
    private readonly Http2RequestEncoder _encoder;
    private readonly Dictionary<int, HttpRequestMessage>? _correlationMap;

    public Http20ConnectionStage(int initialRecvWindowSize = 1_048_576, int maxConcurrentStreams = 100,
        int maxHeaderSize = DefaultMaxHeaderSize,
        int maxTotalHeaderSize = DefaultMaxTotalHeaderSize)
    {
        _initialRecvWindowSize = initialRecvWindowSize;
        _maxConcurrentStreams = maxConcurrentStreams;
        _maxHeaderSize = maxHeaderSize;
        _maxTotalHeaderSize = maxTotalHeaderSize;
        _encoder = new Http2RequestEncoder();
        _correlationMap = new Dictionary<int, HttpRequestMessage>();
    }

    public override Http20ConnectionShape Shape => new(_inServer, _outResponse, _inApp, _outServer, _outSignal);


    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    // Maximum capacity for the stream state pool to prevent OOM when maxConcurrentStreams is unlimited (int.MaxValue)
    private const int MaxStatePoolCapacity = 1000;

    // Stream state (inlined from Http20StreamStage)
    private sealed class StreamState
    {
        private readonly MemoryPool<byte> _pool = MemoryPool<byte>.Shared;

        private IMemoryOwner<byte>? _headerOwner;
        private IMemoryOwner<byte>? _bodyOwner;

        public Memory<byte> HeaderBuffer;
        public Memory<byte> BodyBuffer;

        public int HeaderLength;
        public int BodyLength;

        public HttpResponseMessage? Response;

        // Content headers captured during DecodeHeaders, applied when Content is created.
        public List<(string Name, string Value)>? ContentHeaders;

        public void Reset()
        {
            _headerOwner?.Dispose();
            _headerOwner = null;
            _bodyOwner?.Dispose();
            _bodyOwner = null;
            HeaderBuffer = default;
            BodyBuffer = default;
            HeaderLength = 0;
            BodyLength = 0;
            Response = null;
            ContentHeaders?.Clear();
        }

        public (IMemoryOwner<byte>? Owner, int Length) TakeBodyOwnership()
        {
            var owner = _bodyOwner;
            var length = BodyLength;
            _bodyOwner = null;
            BodyLength = 0;
            return (owner, length);
        }

        public void AppendHeader(ReadOnlySpan<byte> data)
        {
            EnsureHeaderCapacity(HeaderLength + data.Length);
            data.CopyTo(HeaderBuffer.Span[HeaderLength..]);
            HeaderLength += data.Length;
        }

        public void AppendBody(ReadOnlySpan<byte> data)
        {
            EnsureBodyCapacity(BodyLength + data.Length);
            data.CopyTo(BodyBuffer.Span[BodyLength..]);
            BodyLength += data.Length;
        }

        private void EnsureHeaderCapacity(int required)
        {
            if (_headerOwner == null || required > HeaderBuffer.Length)
            {
                RentNewHeaderBuffer(required);
            }
        }

        private void EnsureBodyCapacity(int required)
        {
            if (_bodyOwner == null || required > BodyBuffer.Length)
            {
                RentNewBodyBuffer(required);
            }
        }

        private void RentNewHeaderBuffer(int size)
        {
            var newOwner = _pool.Rent(size);
            if (_headerOwner != null)
            {
                HeaderBuffer.Span.CopyTo(newOwner.Memory.Span);
                _headerOwner.Dispose();
            }

            _headerOwner = newOwner;
            HeaderBuffer = newOwner.Memory;
        }

        private void RentNewBodyBuffer(int size)
        {
            var newOwner = _pool.Rent(size);
            if (_bodyOwner != null)
            {
                BodyBuffer.Span.CopyTo(newOwner.Memory.Span);
                _bodyOwner.Dispose();
            }

            _bodyOwner = newOwner;
            BodyBuffer = newOwner.Memory;
        }
    }

    // Shared empty content — reused for headers-only responses with no content headers.
    // Safe to share because HttpContent.Headers is only mutated via ApplyContentHeaders,
    // which skips this instance when state.ContentHeaders is null.
    private static readonly HttpContent SharedEmptyContent = new ByteArrayContent([]);

    // PooledBodyContent moved to TurboHttp.Internal.PooledBodyContent (shared across HTTP/1.1 and HTTP/2)

    // Logic
    private sealed class Logic : GraphStageLogic
    {
        private readonly Http20ConnectionStage _stage;
        private int _statePoolCapacity;

        // Receive window: how many bytes the server is still allowed to send to us.
        // Decremented by incoming DATA; replenished when we emit WINDOW_UPDATE to server.
        private int _recvConnectionWindow;

        // Send window: how many bytes we are allowed to send to the server.
        // Replenished when the server sends WINDOW_UPDATE to us.
        private int _sendConnectionWindow = 65535; // RFC 9113 §6.5.2 default
        private int _initialRecvStreamWindow;
        private int _initialSendStreamWindow = 65535;
        private int _maxConcurrentStreams;
        private int _activeStreams;
        private bool _goAwayReceived;
        private int _nextStreamId = 1;

        private RequestEndpoint _endpoint;

        // Per-stream receive windows (how much the server can still send per stream).
        private readonly Dictionary<int, int> _recvStreamWindows = new();
        private readonly HashSet<int> _activeStreamIds = [];
        private readonly Queue<Http2Frame> _outboundQueue = new();

        private int _pendingConnIncrement;
        private readonly Dictionary<int, int> _pendingStreamIncrements = new();
        private readonly int _windowUpdateThreshold;

        private readonly Dictionary<int, StreamState> _streams = new();
        private readonly Stack<StreamState> _statePool;
        private readonly HpackDecoder _hpack = new();

        // Prevents double-pull when a response is pushed in the current onPush turn.
        private bool _responsePushed;

        public Logic(Http20ConnectionStage stage) : base(stage.Shape)
        {
            _stage = stage;
            _recvConnectionWindow = stage._initialRecvWindowSize;
            _initialRecvStreamWindow = stage._initialRecvWindowSize;
            _maxConcurrentStreams = stage._maxConcurrentStreams;
            // Clamp pool capacity to a reasonable maximum to prevent OOM when maxConcurrentStreams is unlimited
            _statePoolCapacity = Math.Min(
                _maxConcurrentStreams > 0 ? _maxConcurrentStreams : 100,
                MaxStatePoolCapacity);
            _statePool = new(_statePoolCapacity);
            const int MinWindowUpdateThreshold = 8_192;
            const int MaxWindowUpdateThreshold = 262_144; // 256 KB — allows high-bandwidth connections to scale
            _windowUpdateThreshold = Math.Max(
                MinWindowUpdateThreshold,
                Math.Min(MaxWindowUpdateThreshold, stage._initialRecvWindowSize / 4));

            SetHandler(stage._inServer, onPush: () =>
            {
                var frame = Grab(stage._inServer);
                _responsePushed = false;

                switch (frame)
                {
                    case SettingsFrame settings:
                        HandleSettings(settings);
                        break;

                    case DataFrame data:
                        if (!HandleInboundData(data))
                        {
                            return;
                        }

                        HandleData(data);

                        if (data.EndStream)
                        {
                            CloseStream(data.StreamId);
                        }

                        break;

                    case HeadersFrame headers:
                        HandleHeaders(headers);

                        if (headers.EndStream && _streams.TryGetValue(headers.StreamId, out _))
                        {
                            // EndStream on HEADERS with no prior body — stream closes after DecodeHeaders
                        }

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
                        Pull(stage._inServer);
                        return;

                    case GoAwayFrame goAway:
                        _goAwayReceived = true;
                        Log.Warning(
                            "Http20ConnectionStage: RFC 9113 §6.8 — GOAWAY received (lastStreamId={0}, errorCode={1}). Triggering reconnect.",
                            goAway.LastStreamId, goAway.ErrorCode);
                        Emit(_stage._outSignal, ConnectionReuseItem.Rent(_endpoint,
                            ConnectionReuseDecision.Close("RFC 9113 §6.8: GOAWAY received")));
                        break;
                }

                // If we pushed a response this turn, wait for OutResponse.onPull to pull again.
                // For all other frames (control frames, partial data/headers), re-pull immediately.
                if (!_responsePushed)
                {
                    Pull(stage._inServer);
                }
            }, onUpstreamFinish: () =>
            {
                Log.Debug("Http20ConnectionStage: Completing stage due to server inlet upstream finish.");
                CompleteStage();
            }, onUpstreamFailure: ex =>
            {
                Log.Warning("Http20ConnectionStage: Server inlet upstream failure: {0}", ex.Message);
                Log.Debug("Http20ConnectionStage: Failing stage due to server inlet upstream failure.");
                FailStage(ex);
            });

            SetHandler(stage._outResponse, onPull: () => { Pull(stage._inServer); });

            SetHandler(stage._inApp, onPush: () =>
            {
                var request = Grab(stage._inApp);
                var streamId = _nextStreamId;
                _nextStreamId += 2;

                if (_goAwayReceived)
                {
                    Log.Warning(
                        "Http20ConnectionStage: RFC 9113 §6.8 — GOAWAY received; dropping new request (stream {0}).",
                        streamId);
                    // TryPullRequest guards on _goAwayReceived — no further pull issued.
                    return;
                }

                var endpoint = request.RequestUri is not null
                    ? RequestEndpoint.FromRequest(request)
                    : RequestEndpoint.Default;

                if (_endpoint == default && endpoint != default)
                {
                    _endpoint = endpoint;
                }

                if (_stage._correlationMap is { } map)
                {
                    map[streamId] = request;
                }

                if (request.RequestUri is null)
                {
                    _activeStreams++;
                    _activeStreamIds.Add(streamId);
                    Emit(stage._outSignal, StreamAcquireItem.Rent(_endpoint));
                    TryPullRequest();
                    return;
                }

                var (_, frames) = _stage._encoder.Encode(request, streamId);
                var first = true;
                foreach (var frame in frames)
                {
                    if (first)
                    {
                        frame.Endpoint = endpoint;
                        first = false;

                        if (frame is HeadersFrame headers)
                        {
                            _activeStreams++;
                            _activeStreamIds.Add(headers.StreamId);
                            Emit(stage._outSignal, StreamAcquireItem.Rent(_endpoint));
                        }
                    }

                    // Batch-enqueue without draining per frame — drain once at the end.
                    _outboundQueue.Enqueue(frame);
                }

                // Drain one frame now (if outServer has demand) and immediately pull the
                // next request without waiting for the queue to empty (eager re-pull).
                // While req[N]'s remaining frames drain via subsequent OutServer.onPull events,
                // req[N+1] is already being encoded in the next scheduling turn.
                TryDrainOutbound();
                TryPullRequest();
            }, onUpstreamFinish: () =>
            {
                // Request stream finished — keep stage alive to receive server responses.
            }, onUpstreamFailure: ex =>
            {
                Log.Warning("Http20ConnectionStage: App inlet upstream failure: {0}", ex.Message);
                Log.Debug("Http20ConnectionStage: Failing stage due to app inlet upstream failure.");
                FailStage(ex);
            });

            SetHandler(stage._outServer, onPull: () =>
            {
                TryDrainOutbound();
                TryPullRequest();
            });

            SetHandler(stage._outSignal, onPull: () =>
            {
                // Demand-driven by downstream MergePreferred; no action needed.
            });
        }

        private void EnqueueOutbound(Http2Frame frame)
        {
            _outboundQueue.Enqueue(frame);
            TryDrainOutbound();
        }

        private void TryDrainOutbound()
        {
            // Drain as many queued frames as possible while downstream has demand.
            // Previously only drained 1 frame per pull, causing HOL-blocking when
            // multiple streams' frames were queued behind each other.
            while (_outboundQueue.Count > 0 && IsAvailable(_stage._outServer))
            {
                Push(_stage._outServer, _outboundQueue.Dequeue());
            }
        }

        private void HandleSettings(SettingsFrame frame)
        {
            if (frame.IsAck)
            {
                return;
            }

            foreach (var (key, value) in frame.Parameters)
            {
                if (key == SettingsParameter.InitialWindowSize)
                {
                    _initialSendStreamWindow = (int)value;
                }

                if (key == SettingsParameter.MaxConcurrentStreams)
                {
                    _maxConcurrentStreams = (int)value;
                    // Update pool capacity to match the server's maxConcurrentStreams
                    _statePoolCapacity = Math.Min(
                        _maxConcurrentStreams > 0 ? _maxConcurrentStreams : 100,
                        MaxStatePoolCapacity);
                    Emit(_stage._outSignal, new MaxConcurrentStreamsItem(_maxConcurrentStreams)
                    {
                        Key = _endpoint
                    });
                }
            }

            EnqueueOutbound(new SettingsFrame([], isAck: true));
        }

        private bool HandleInboundData(DataFrame frame)
        {
            var dataLength = frame.Data.Length;

            _recvConnectionWindow -= dataLength;

            _recvStreamWindows.TryAdd(frame.StreamId, _initialRecvStreamWindow);
            _recvStreamWindows[frame.StreamId] -= dataLength;

            if (_recvConnectionWindow < 0)
            {
                Log.Warning(
                    "Http20ConnectionStage: RFC 9113 §6.9 — connection flow control window exceeded by {0} bytes. Triggering reconnect.",
                    -_recvConnectionWindow);
                Emit(_stage._outSignal, ConnectionReuseItem.Rent(_endpoint,
                    ConnectionReuseDecision.Close("RFC 9113 §6.9: connection window exceeded")));
                Pull(_stage._inServer);
                return false;
            }

            if (_recvStreamWindows[frame.StreamId] < 0)
            {
                Log.Warning(
                    "Http20ConnectionStage: RFC 9113 §6.9 — stream {0} flow control window exceeded by {1} bytes. Triggering reconnect.",
                    frame.StreamId, -_recvStreamWindows[frame.StreamId]);
                Emit(_stage._outSignal, ConnectionReuseItem.Rent(_endpoint,
                    ConnectionReuseDecision.Close("RFC 9113 §6.9: stream window exceeded")));
                Pull(_stage._inServer);
                return false;
            }

            if (dataLength > 0)
            {
                _pendingConnIncrement += dataLength;
                _pendingStreamIncrements.TryAdd(frame.StreamId, 0);
                _pendingStreamIncrements[frame.StreamId] += dataLength;

                if (_pendingConnIncrement >= _windowUpdateThreshold)
                {
                    var increment = _pendingConnIncrement;
                    _recvConnectionWindow += increment; // Replenish our receive window before notifying server.
                    EnqueueOutbound(new WindowUpdateFrame(0, increment));
                    _pendingConnIncrement = 0;
                }

                if (_pendingStreamIncrements[frame.StreamId] >= _windowUpdateThreshold)
                {
                    var increment = _pendingStreamIncrements[frame.StreamId];
                    _recvStreamWindows[frame.StreamId] += increment; // Replenish stream receive window.
                    EnqueueOutbound(new WindowUpdateFrame(frame.StreamId, increment));
                    _pendingStreamIncrements[frame.StreamId] = 0;
                }
            }

            return true;
        }

        private void HandlePing(PingFrame ping)
        {
            if (!ping.IsAck)
            {
                EnqueueOutbound(new PingFrame(ping.Data, true));
            }
        }

        private void HandleWindowUpdate(WindowUpdateFrame frame)
        {
            // Server's WINDOW_UPDATE increases our SEND budget (not our receive window).
            if (frame.StreamId == 0)
            {
                _sendConnectionWindow += frame.Increment;
            }
            // Stream-level WINDOW_UPDATE from server grants us additional send credit for that stream.
            // We don't currently enforce per-stream send limits, so nothing to update here.
        }

        private void CloseStream(int streamId)
        {
            if (_activeStreamIds.Remove(streamId))
            {
                _activeStreams--;
                TryDrainOutbound();
                TryPullRequest(); // Budget freed — pull next request immediately.
            }

            if (_pendingStreamIncrements.TryGetValue(streamId, out var pending) && pending > 0)
            {
                EnqueueOutbound(new WindowUpdateFrame(streamId, pending));
            }

            _pendingStreamIncrements.Remove(streamId);
            _recvStreamWindows.Remove(streamId);
        }

        private void TryPullRequest()
        {
            if (!_goAwayReceived
                && _activeStreams < _maxConcurrentStreams
                && !HasBeenPulled(_stage._inApp)
                && !IsClosed(_stage._inApp))
            {
                Pull(_stage._inApp);
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
                Log.Warning("Http20ConnectionStage: Received CONTINUATION for unknown stream {0} — dropping.",
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
                Log.Warning("Http20ConnectionStage: Received DATA for unknown stream {0} — dropping.", frame.StreamId);
                return;
            }

            state.AppendBody(frame.Data.Span);

            if (!frame.EndStream)
            {
                return;
            }

            var response = state.Response ?? new HttpResponseMessage();

            var (bodyOwner, bodyLength) = state.TakeBodyOwnership();
            response.Content = bodyOwner is null
                ? (state.ContentHeaders is null ? SharedEmptyContent : new ByteArrayContent([]))
                : new PooledBodyContent(bodyOwner, bodyLength);
            ApplyContentHeaders(response, state);

            if (_stage._correlationMap?.Remove(frame.StreamId, out var req) == true)
            {
                response.RequestMessage = req;
            }

            _responsePushed = true;
            Push(_stage._outResponse, response);

            _streams.Remove(frame.StreamId);
            ReturnState(state);
        }

        private void DecodeHeaders(int streamId, bool endStream)
        {
            if (!_streams.TryGetValue(streamId, out var state))
            {
                Log.Warning("Http20ConnectionStage: DecodeHeaders called for unknown stream {0} — dropping.", streamId);
                return;
            }

            var headers = _hpack.Decode(state.HeaderBuffer[..state.HeaderLength].Span);

            var maxHeaderSize = _stage._maxHeaderSize;
            var maxTotalHeaderSize = _stage._maxTotalHeaderSize;
            var totalHeaderSize = 0;

            var response = new HttpResponseMessage();

            foreach (var h in headers)
            {
                var headerSize = h.Name.Length + h.Value.Length;

                if (headerSize > maxHeaderSize)
                {
                    throw new Http2Exception(
                        $"RFC 9113 §10.5.1: Single header field size {headerSize} bytes " +
                        $"exceeds MaxHeaderSize limit ({maxHeaderSize} bytes) " +
                        $"on stream {streamId} — header '{h.Name}'.",
                        Http2ErrorCode.FrameSizeError,
                        Http2ErrorScope.Stream,
                        streamId);
                }

                totalHeaderSize += headerSize;

                if (totalHeaderSize > maxTotalHeaderSize)
                {
                    throw new Http2Exception(
                        $"RFC 9113 §10.5.1: Total header block size {totalHeaderSize} bytes " +
                        $"exceeds MaxTotalHeaderSize limit ({maxTotalHeaderSize} bytes) " +
                        $"on stream {streamId}.",
                        Http2ErrorCode.FrameSizeError,
                        Http2ErrorScope.Stream,
                        streamId);
                }

                if (h.Name == ":status")
                {
                    response.StatusCode = (HttpStatusCode)int.Parse(h.Value);
                }
                else if (!h.Name.StartsWith(':'))
                {
                    response.Headers.TryAddWithoutValidation(h.Name, h.Value);

                    if (IsContentHeader(h.Name))
                    {
                        state.ContentHeaders ??= [];
                        state.ContentHeaders.Add((h.Name, h.Value));
                    }
                }
            }

            state.Response = response;

            if (!endStream)
            {
                return;
            }

            // Headers-only response (no body).
            response.Content = state.ContentHeaders is null
                ? SharedEmptyContent
                : new ByteArrayContent([]);
            ApplyContentHeaders(response, state);

            if (_stage._correlationMap?.Remove(streamId, out var req) == true)
            {
                response.RequestMessage = req;
            }

            _responsePushed = true;
            Push(_stage._outResponse, response);

            _streams.Remove(streamId);
            ReturnState(state);
        }

        private static void ApplyContentHeaders(HttpResponseMessage response, StreamState state)
        {
            if (state.ContentHeaders is null)
            {
                return;
            }

            foreach (var (name, value) in state.ContentHeaders)
            {
                response.Content.Headers.TryAddWithoutValidation(name, value);
            }
        }

        private static bool IsContentHeader(string name) =>
            name.StartsWith("content-", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("allow", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("expires", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("last-modified", StringComparison.OrdinalIgnoreCase);
    }
}