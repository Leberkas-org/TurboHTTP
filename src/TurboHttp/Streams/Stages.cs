using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Channels;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Stage;
using Servus.Akka.IO;
using TurboHttp.Protocol;

namespace TurboHttp.Streams;

public class Stages
{
    public sealed class HostRoutingFlow : GraphStage<FlowShape<HttpRequestMessage, HttpResponseMessage>>
    {
        public Inlet<HttpRequestMessage> Inlet = new("pool.in");
        public Outlet<HttpResponseMessage> Outlet = new("pool.out");

        public override FlowShape<HttpRequestMessage, HttpResponseMessage> Shape { get; }

        public HostRoutingFlow()
        {
            Shape = new FlowShape<HttpRequestMessage, HttpResponseMessage>(Inlet, Outlet);
        }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
            => new Logic(this);

        private sealed class Logic : GraphStageLogic
        {
            private readonly HostRoutingFlow _stage;
            private readonly Dictionary<string, HostConnectionPool> _pools = new();
            private readonly Queue<HttpResponseMessage> _responseBuffer = new();
            private bool _downstreamWaiting;

            private Action<HttpResponseMessage>? _onResponse;

            public Logic(HostRoutingFlow stage) : base(stage.Shape)
            {
                _stage = stage;

                SetHandler(stage.Inlet,
                    onPush: () =>
                    {
                        var request = Grab(stage.Inlet);
                        var uri = request.RequestUri!;
                        int port;
                        if (uri.Port is -1)
                        {
                            port = uri.Scheme == "https" ? 443 : 80;
                        }
                        else
                        {
                            port = uri.Port;
                        }

                        var pool = GetOrCreatePool(new TcpOptions
                        {
                            Host = uri.Host,
                            Port = port,
                            AddressFamily = uri.HostNameType switch
                            {
                                UriHostNameType.IPv4 => AddressFamily.InterNetwork,
                                UriHostNameType.IPv6 => AddressFamily.InterNetworkV6,
                                _ => AddressFamily.Unspecified
                            }
                        });

                        pool.Send(request);
                        Pull(stage.Inlet);
                    });

                SetHandler(stage.Outlet,
                    onPull: () =>
                    {
                        if (_responseBuffer.TryDequeue(out var response))
                        {
                            Push(stage.Outlet, response);
                        }
                        else
                        {
                            _downstreamWaiting = true;
                        }
                    });
            }

            public override void PreStart()
            {
                _onResponse = GetAsyncCallback<HttpResponseMessage>(response =>
                {
                    if (_downstreamWaiting)
                    {
                        _downstreamWaiting = false;
                        Push(_stage.Outlet, response);
                    }
                    else
                    {
                        _responseBuffer.Enqueue(response);
                    }
                });

                Pull(_stage.Inlet);
            }

            private HostConnectionPool GetOrCreatePool(TcpOptions options)
            {
                var host = options.Host;
                var port = options.Port;

                var key = $"{host}:{port}";

                if (_pools.TryGetValue(key, out var pool))
                {
                    return pool;
                }

                var system = (Materializer as ActorMaterializer)!.System;

                var newPool = new HostConnectionPool(options, system, _onResponse!);

                _pools[key] = newPool;
                return newPool;
            }
        }
    }

    public sealed class ConnectionStage : GraphStage<FlowShape<(IMemoryOwner<byte>, int), (IMemoryOwner<byte>, int)>>
    {
        public IActorRef ClientManager { get; }
        public TcpOptions Options { get; }

        public Inlet<(IMemoryOwner<byte>, int)> Inlet = new("tcp.in");
        public Outlet<(IMemoryOwner<byte>, int)> Outlet = new("tcp.out");

        public override FlowShape<(IMemoryOwner<byte>, int), (IMemoryOwner<byte>, int)> Shape { get; }

        public ConnectionStage(IActorRef clientManager, TcpOptions options)
        {
            ClientManager = clientManager;
            Options = options;
            Shape = new FlowShape<(IMemoryOwner<byte>, int), (IMemoryOwner<byte>, int)>(Inlet, Outlet);
        }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
            => new Logic(this);

        private sealed class Logic : GraphStageLogic
        {
            private readonly ConnectionStage _stage;
            private StageActor _self;
            private bool _connected;
            private bool _downstreamWaiting;
            private int _reconnectAttempts;
            private readonly Queue<(IMemoryOwner<byte>, int)> _outboundBuffer = new();
            private ChannelWriter<(IMemoryOwner<byte>, int)>? _outboundWriter;
            private ChannelReader<(IMemoryOwner<byte>, int)>? _inboundReader;

            private Action? _onReadReady;
            private Action? _onDisconnected;

            public Logic(ConnectionStage stage) : base(stage.Shape)
            {
                _stage = stage;

                SetHandler(stage.Inlet,
                    onPush: () =>
                    {
                        var chunk = Grab(stage.Inlet);
                        if (_connected)
                        {
                            _outboundWriter!.TryWrite(chunk);
                        }
                        else
                        {
                            _outboundBuffer.Enqueue(chunk);
                            _stage.ClientManager.Tell(new ClientManager.CreateTcpRunner(_stage.Options, _self.Ref));
                        }
                    },
                    onUpstreamFinish: CompleteStage,
                    onUpstreamFailure: FailStage);

                SetHandler(stage.Outlet,
                    onPull: () =>
                    {
                        _downstreamWaiting = true;
                        if (_connected)
                        {
                            TryReadInbound();
                        }
                    },
                    onDownstreamFinish: _ => CompleteStage());
            }

            public override void PreStart()
            {
                _onReadReady = GetAsyncCallback(TryReadInbound);
                _onDisconnected = GetAsyncCallback(HandleDisconnected);

                _self = GetStageActor(OnMessage);
            }

            private void OnMessage((IActorRef sender, object msg) args)
            {
                switch (args.msg)
                {
                    case ClientRunner.ClientConnected connected:
                        _connected = true;
                        _reconnectAttempts = 0;
                        _inboundReader = connected.InboundReader;
                        _outboundWriter = connected.OutboundWriter;

                        while (_outboundBuffer.TryDequeue(out var chunk))
                        {
                            _outboundWriter.TryWrite(chunk);
                        }

                        Pull(_stage.Inlet);

                        if (_downstreamWaiting)
                        {
                            TryReadInbound();
                        }

                        break;

                    case ClientRunner.ClientDisconnected:
                        HandleDisconnected();
                        break;
                }
            }

            private void TryReadInbound()
            {
                if (_inboundReader is null) return;

                if (_inboundReader.TryRead(out var chunk))
                {
                    _downstreamWaiting = false;
                    Push(_stage.Outlet, chunk);
                    return;
                }

                _inboundReader
                    .WaitToReadAsync()
                    .AsTask()
                    .ContinueWith(t =>
                    {
                        if (t is { IsCompletedSuccessfully: true, Result: true })
                        {
                            _onReadReady!();
                        }
                        else
                        {
                            _onDisconnected!();
                        }
                    }, TaskContinuationOptions.ExecuteSynchronously);
            }

            private void HandleDisconnected()
            {
                _connected = false;
                _inboundReader = null;
                _outboundWriter = null;

                var delay = TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, _reconnectAttempts++)));
                var stageRef = _self.Ref;
                var options = _stage.Options;
                var manager = _stage.ClientManager;

                Task.Delay(delay).ContinueWith(_ =>
                    manager.Tell(new ClientManager.CreateTcpRunner(options, stageRef)));
            }

            public override void PostStop()
            {
                while (_outboundBuffer.TryDequeue(out var chunk))
                {
                    chunk.Item1.Dispose();
                }
            }
        }
    }

    public sealed class Http10EncoderStage : GraphStage<FlowShape<HttpRequestMessage, (IMemoryOwner<byte>, int)>>
    {
        public Inlet<HttpRequestMessage> In { get; } = new("http10.encoder.in");
        public Outlet<(IMemoryOwner<byte>, int)> Out { get; } = new("http10.encoder.out");

        public override FlowShape<HttpRequestMessage, (IMemoryOwner<byte>, int)> Shape { get; }

        public Http10EncoderStage()
        {
            Shape = new FlowShape<HttpRequestMessage, (IMemoryOwner<byte>, int)>(In, Out);
        }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
            => new Logic(this);

        private sealed class Logic : GraphStageLogic
        {
            private const int MinBufferSize = 4 * 1024; // 4 KB
            private const int MaxBufferSize = 256 * 1024; // 256 KB

            public Logic(Http10EncoderStage stage) : base(stage.Shape)
            {
                SetHandler(stage.In,
                    onPush: () =>
                    {
                        var request = Grab(stage.In);

                        try
                        {
                            var contentLength = Convert.ToInt32(request.Content?.Headers.ContentLength ?? 0);
                            var estimatedSize = MinBufferSize + contentLength;
                            var bufferSize = Math.Min(estimatedSize, MaxBufferSize);
                            var owner = MemoryPool<byte>.Shared.Rent(bufferSize);
                            var buffer = owner.Memory;

                            var written = Http10Encoder.Encode(request, ref buffer);

                            Push(stage.Out, (owner, written));
                        }
                        catch (Exception ex)
                        {
                            FailStage(ex);
                        }
                    },
                    onUpstreamFinish: CompleteStage,
                    onUpstreamFailure: FailStage);

                SetHandler(stage.Out, onPull: () => Pull(stage.In), onDownstreamFinish: _ => CompleteStage());
            }
        }
    }

    public sealed class Http10DecoderStage : GraphStage<FlowShape<(IMemoryOwner<byte>, int), HttpResponseMessage>>
    {
        public Inlet<(IMemoryOwner<byte>, int)> In { get; } = new("http10.decoder.in");
        public Outlet<HttpResponseMessage> Out { get; } = new("http10.decoder.out");

        public override FlowShape<(IMemoryOwner<byte>, int), HttpResponseMessage> Shape { get; }

        public Http10DecoderStage()
        {
            Shape = new FlowShape<(IMemoryOwner<byte>, int), HttpResponseMessage>(In, Out);
        }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
            => new Logic(this);

        private sealed class Logic : GraphStageLogic
        {
            private readonly Http10Decoder _decoder = new();

            public Logic(Http10DecoderStage stage) : base(stage.Shape)
            {
                SetHandler(stage.In,
                    onPush: () =>
                    {
                        var (owner, length) = Grab(stage.In);

                        try
                        {
                            var data = owner.Memory[..length];

                            if (_decoder.TryDecode(data, out var response) && response is not null)
                            {
                                owner.Dispose();
                                Push(stage.Out, response);
                            }
                            else
                            {
                                // Not enough data yet – return the buffer and wait for more
                                owner.Dispose();
                                Pull(stage.In);
                            }
                        }
                        catch (Exception ex)
                        {
                            owner.Dispose();
                            FailStage(ex);
                        }
                    },
                    onUpstreamFinish: () =>
                    {
                        // Flush any partial response buffered in the decoder
                        if (_decoder.TryDecodeEof(out var response) && response is not null)
                        {
                            Emit(stage.Out, response, CompleteStage);
                        }
                        else
                        {
                            CompleteStage();
                        }
                    },
                    onUpstreamFailure: FailStage);

                SetHandler(stage.Out,
                    onPull: () => Pull(stage.In),
                    onDownstreamFinish: _ => CompleteStage());
            }
        }
    }
    
    public sealed class Http11EncoderStage : GraphStage<FlowShape<HttpRequestMessage, (IMemoryOwner<byte>, int)>>
    {
        public Inlet<HttpRequestMessage> In { get; } = new("http11.encoder.in");
        public Outlet<(IMemoryOwner<byte>, int)> Out { get; } = new("http11.encoder.out");

        public Http11EncoderStage()
        {
            Shape = new FlowShape<HttpRequestMessage, (IMemoryOwner<byte>, int)>(In, Out);
        }

        public override FlowShape<HttpRequestMessage, (IMemoryOwner<byte>, int)> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new Logic(this);
        }

        private sealed class Logic : GraphStageLogic
        {
            private const int MinBufferSize = 4 * 1024; // 4 KB
            private const int MaxBufferSize = 256 * 1024; // 256 KB

            public Logic(Http11EncoderStage stage) : base(stage.Shape)
            {
                SetHandler(stage.In,
                    onPush: () =>
                    {
                        var request = Grab(stage.In);

                        try
                        {
                            var contentLength = Convert.ToInt32(request.Content?.Headers.ContentLength ?? 0);
                            var estimatedSize = MinBufferSize + contentLength;
                            var bufferSize = Math.Min(estimatedSize, MaxBufferSize);
                            var owner = MemoryPool<byte>.Shared.Rent(bufferSize);
                            var buffer = owner.Memory;

                            var written = Http11Encoder.Encode(request, ref buffer);

                            Push(stage.Out, (owner, written));
                        }
                        catch (Exception ex)
                        {
                            FailStage(ex);
                        }
                    },
                    onUpstreamFinish: CompleteStage,
                    onUpstreamFailure: FailStage);

                SetHandler(stage.Out,
                    onPull: () => Pull(stage.In),
                    onDownstreamFinish: _ => CompleteStage());
            }
        }
    }

    public sealed class Http11DecoderStage : GraphStage<FlowShape<(IMemoryOwner<byte>, int), HttpResponseMessage>>
    {
        public Inlet<(IMemoryOwner<byte>, int)> In { get; } = new("http10.decoder.in");
        public Outlet<HttpResponseMessage> Out { get; } = new("http10.decoder.out");

        public Http11DecoderStage()
        {
            Shape = new FlowShape<(IMemoryOwner<byte>, int), HttpResponseMessage>(In, Out);
        }

        public override FlowShape<(IMemoryOwner<byte>, int), HttpResponseMessage> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new Logic(this);
        }

        private sealed class Logic : GraphStageLogic
        {
            private readonly Http11Decoder _decoder = new();

            public Logic(Http11DecoderStage stage) : base(stage.Shape)
            {
                SetHandler(stage.In,
                    onPush: () =>
                    {
                        var (owner, length) = Grab(stage.In);

                        try
                        {
                            var data = owner.Memory[..length];

                            if (_decoder.TryDecode(data, out var response))
                            {
                                owner.Dispose();
                                EmitMultiple(stage.Out, response);
                            }
                            else
                            {
                                // Not enough data yet – return the buffer and wait for more
                                owner.Dispose();
                                Pull(stage.In);
                            }
                        }
                        catch (Exception ex)
                        {
                            owner.Dispose();
                            FailStage(ex);
                        }
                    },
                    onUpstreamFinish: CompleteStage,
                    onUpstreamFailure: FailStage);

                SetHandler(stage.Out,
                    onPull: () => Pull(stage.In),
                    onDownstreamFinish: _ => CompleteStage());
            }
        }
    }

    public sealed class Request2Http2FrameStage : GraphStage<FlowShape<HttpRequestMessage, Http2Frame>>
    {
        private readonly Inlet<HttpRequestMessage> _in = new("req.in");

        private readonly Outlet<Http2Frame> _out = new("req.out");

        public override FlowShape<HttpRequestMessage, Http2Frame> Shape => new(_in, _out);

        protected override GraphStageLogic CreateLogic(Attributes attr) => new Logic(this);

        private sealed class Logic : GraphStageLogic
        {
            public Logic(Request2Http2FrameStage stage) : base(stage.Shape)
            {
                SetHandler(stage._in, () =>
                {
                    var req = Grab(stage._in);

                    var headers = new List<(SettingsParameter, uint)>();

                    foreach (var h in req.Headers)
                    {
                    }

                    var frame =
                        new HeadersFrame(streamId: 1, headerBlock: Array.Empty<byte>(),
                            endStream: req.Content == null);

                    Push(stage._out, frame);
                });

                SetHandler(stage._out, () => { Pull(stage._in); });
            }
        }
    }

    public sealed class Http2FrameEncoderStage : GraphStage<FlowShape<Http2Frame, (IMemoryOwner<byte>, int)>>
    {
        private readonly Inlet<Http2Frame> Inlet = new("frameEncoder.in");

        private readonly Outlet<(IMemoryOwner<byte>, int)> Outlet = new("frameEncoder.out");

        public override FlowShape<Http2Frame, (IMemoryOwner<byte>, int)> Shape =>
            new(Inlet, Outlet);

        protected override GraphStageLogic CreateLogic(Attributes attributes)
            => new Logic(this);

        private sealed class Logic : GraphStageLogic
        {
            private readonly Http2FrameEncoderStage _stage;

            public Logic(Http2FrameEncoderStage stage)
                : base(stage.Shape)
            {
                _stage = stage;

                SetHandler(stage.Inlet, () =>
                {
                    var frame = Grab(stage.Inlet);

                    var owner = MemoryPool<byte>.Shared.Rent(frame.SerializedSize);
                    var span = owner.Memory.Span;

                    frame.WriteTo(ref span);

                    Push(stage.Outlet, (owner, frame.SerializedSize));
                });

                SetHandler(stage.Outlet, () => Pull(stage.Inlet));
            }
        }
    }

    public sealed class Http2FrameDecoderStage : GraphStage<FlowShape<(IMemoryOwner<byte>, int), Http2Frame>>
    {
        public Inlet<(IMemoryOwner<byte>, int)> Inlet = new("http20.tcp.in");

        public Outlet<Http2Frame> Outlet = new("http20.frame.out");

        public override FlowShape<(IMemoryOwner<byte>, int), Http2Frame> Shape => new(Inlet, Outlet);

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new Logic(this);
        }

        private sealed class Logic : GraphStageLogic
        {
            private readonly MemoryPool<byte> _pool = MemoryPool<byte>.Shared;

            private IMemoryOwner<byte> _bufferOwner;
            private Memory<byte> _buffer;
            private int _count;

            public Logic(Http2FrameDecoderStage stage) : base(stage.Shape)
            {
                SetHandler(stage.Inlet, onPush: () =>
                {
                    var (owner, length) = Grab(stage.Inlet);

                    try
                    {
                        Append(owner.Memory.Span[..length]);
                    }
                    finally
                    {
                        owner.Dispose();
                    }

                    TryParse(stage);
                });

                SetHandler(stage.Outlet, onPull: () => Pull(stage.Inlet));
            }

            private void Append(ReadOnlySpan<byte> data)
            {
                EnsureCapacity(_count + data.Length);

                data.CopyTo(_buffer.Span[_count..]);
                _count += data.Length;
            }

            private void EnsureCapacity(int required)
            {
                if (required <= _buffer.Length) return;
                var newSize = Math.Max(required, _buffer.Length * 2);

                var newOwner = _pool.Rent(newSize);

                _buffer.Span.CopyTo(newOwner.Memory.Span);
                _bufferOwner.Dispose();

                _bufferOwner = newOwner;
                _buffer = newOwner.Memory;
            }

            private void TryParse(Http2FrameDecoderStage stage)
            {
                while (true)
                {
                    if (_count < 9)
                    {
                        return;
                    }

                    var span = _buffer.Span[.._count];

                    var length = (span[0] << 16) | (span[1] << 8) | span[2];

                    if (_count < 9 + length)
                    {
                        return;
                    }

                    var type = (FrameType)span[3];
                    var flags = span[4];

                    var streamId = BinaryPrimitives.ReadInt32BigEndian(span.Slice(5, 4)) & 0x7FFFFFFF;

                    var payload = span.Slice(9, length).ToArray();

                    ShiftBuffer(9 + length);

                    var frame = CreateFrame(type, flags, streamId, payload);

                    Emit(stage.Outlet, frame);
                }
            }

            private Http2Frame CreateFrame(FrameType type, byte flags, int streamId, byte[] payload)
            {
                switch (type)
                {
                    case FrameType.Data:
                        return new DataFrame(streamId, payload, (flags & 0x1) != 0);

                    case FrameType.Headers:
                        return new HeadersFrame(streamId, payload, (flags & 0x1) != 0, (flags & 0x4) != 0);

                    case FrameType.Continuation:
                        return new ContinuationFrame(streamId, payload, (flags & 0x4) != 0);

                    case FrameType.Ping:
                        return new PingFrame(payload, (flags & 0x1) != 0);

                    default:
                        throw new Exception("Unsupported frame");
                }
            }

            private void ShiftBuffer(int consumed)
            {
                _count -= consumed;

                if (_count > 0)
                {
                    _buffer.Span.Slice(consumed, _count).CopyTo(_buffer.Span);
                }
            }
        }
    }

    public sealed class Http2ConnectionStage : GraphStage<BidiShape<Http2Frame, Http2Frame, Http2Frame, Http2Frame>>
    {
        public readonly Inlet<Http2Frame> InletRaw = new("h2.server.in");

        public readonly Outlet<Http2Frame> OutletStream = new("h2.app.out");

        public readonly Inlet<Http2Frame> InletRequest = new("h2.app.in");

        public readonly Outlet<Http2Frame> OutletRaw = new("h2.server.out");

        public override BidiShape<Http2Frame, Http2Frame, Http2Frame, Http2Frame> Shape
            => new(InletRaw, OutletStream, InletRequest, OutletRaw);

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
            => new Logic(this);

        private sealed class Logic : GraphStageLogic
        {
            private readonly Http2ConnectionStage _stage;
            private int _connectionWindow = 65535;
            private int _initialStreamWindow = 65535;
            private bool _goAwayReceived;

            private readonly Dictionary<int, int> _streamWindows = new();

            public Logic(Http2ConnectionStage stage) : base(stage.Shape)
            {
                _stage = stage;
                SetHandler(stage.InletRaw, onPush: () =>
                {
                    var frame = Grab(stage.InletRaw);

                    switch (frame)
                    {
                        case SettingsFrame settings:
                            HandleSettings(settings);
                            break;

                        case DataFrame data:
                            HandleInboundData(data);
                            break;

                        case WindowUpdateFrame win:
                            HandleWindowUpdate(win);
                            break;

                        case PingFrame ping:
                            HandlePing(ping);
                            return;

                        case GoAwayFrame:
                            _goAwayReceived = true;
                            break;
                    }

                    Push(stage.OutletStream, frame);
                });

                SetHandler(stage.OutletStream, onPull: () => Pull(stage.InletRaw));

                SetHandler(stage.InletRequest, onPush: () =>
                {
                    var frame = Grab(stage.InletRequest);

                    switch (frame)
                    {
                        case DataFrame data:
                            HandleOutboundData(data);
                            break;
                    }

                    Push(stage.OutletRaw, frame);
                });

                SetHandler(stage.OutletRaw, onPull: () => Pull(stage.InletRequest));
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
                        _initialStreamWindow = (int)value;
                    }
                }

                Emit(_stage.OutletRaw, new SettingsFrame([]));
            }

            private void HandleInboundData(DataFrame frame)
            {
                _connectionWindow -= frame.Data.Length;

                _streamWindows.TryAdd(frame.StreamId, _initialStreamWindow);

                _streamWindows[frame.StreamId] -= frame.Data.Length;

                if (_connectionWindow < 0)
                {
                    FailStage(new Exception("Connection window exceeded"));
                }

                if (_streamWindows[frame.StreamId] < 0)
                {
                    FailStage(new Exception("Stream window exceeded"));
                }

                Emit(_stage.OutletRaw, new WindowUpdateFrame(0, frame.Data.Length));

                Emit(_stage.OutletRaw, new WindowUpdateFrame(frame.StreamId, frame.Data.Length));
            }

            private void HandlePing(PingFrame ping)
            {
                if (!ping.IsAck)
                {
                    Emit(_stage.OutletRaw, new PingFrame(ping.Data, true));
                }
            }

            private void HandleWindowUpdate(WindowUpdateFrame frame)
            {
                if (frame.StreamId == 0)
                {
                    _connectionWindow += frame.Increment;
                }
                else
                {
                    _streamWindows.TryAdd(frame.StreamId, _initialStreamWindow);

                    _streamWindows[frame.StreamId] += frame.Increment;
                }
            }

            private void HandleOutboundData(DataFrame frame)
            {
                _connectionWindow -= frame.Data.Length;

                if (_connectionWindow < 0)
                {
                    FailStage(new Exception("Outbound flow control exceeded"));
                }
            }
        }
    }

    public sealed class Http2StreamStage : GraphStage<FlowShape<Http2Frame, HttpResponseMessage>>
    {
        private readonly Inlet<Http2Frame> Inlet = new("h2.stream.in");

        private readonly Outlet<HttpResponseMessage> Outlet = new("h2.stream.out");

        public override FlowShape<Http2Frame, HttpResponseMessage> Shape => new(Inlet, Outlet);

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
            => new Logic(this);

        private sealed class Logic : GraphStageLogic
        {
            private sealed class StreamState : IDisposable
            {
                private readonly MemoryPool<byte> _pool;

                public IMemoryOwner<byte>? HeaderOwner;
                public IMemoryOwner<byte>? BodyOwner;

                public Memory<byte> HeaderBuffer;
                public Memory<byte> BodyBuffer;

                public int HeaderLength;
                public int BodyLength;

                public HttpResponseMessage? Response;

                public StreamState(MemoryPool<byte> pool)
                {
                    _pool = pool;
                }

                public void Dispose()
                {
                    HeaderOwner?.Dispose();
                    BodyOwner?.Dispose();
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
                    if (HeaderOwner == null || required > HeaderBuffer.Length)
                    {
                        RentNewHeaderBuffer(required);
                    }
                }

                private void EnsureBodyCapacity(int required)
                {
                    if (BodyOwner == null || required > BodyBuffer.Length)
                    {
                        RentNewBodyBuffer(required);
                    }
                }

                private void RentNewHeaderBuffer(int size)
                {
                    var newOwner = _pool.Rent(size);

                    if (HeaderOwner != null)
                    {
                        HeaderBuffer.Span.CopyTo(newOwner.Memory.Span);
                        HeaderOwner.Dispose();
                    }

                    HeaderOwner = newOwner;
                    HeaderBuffer = newOwner.Memory;
                }

                private void RentNewBodyBuffer(int size)
                {
                    var newOwner = _pool.Rent(size);

                    if (BodyOwner != null)
                    {
                        BodyBuffer.Span.CopyTo(newOwner.Memory.Span);
                        BodyOwner.Dispose();
                    }

                    BodyOwner = newOwner;
                    BodyBuffer = newOwner.Memory;
                }
            }

            private readonly Http2StreamStage _stage;
            private readonly Dictionary<int, StreamState> _streams = new();

            private readonly HpackDecoder _hpack = new();

            public Logic(Http2StreamStage stage) : base(stage.Shape)
            {
                _stage = stage;
                SetHandler(stage.Inlet, () =>
                {
                    var frame = Grab(stage.Inlet);
                    _streams.TryAdd(frame.StreamId, new StreamState(MemoryPool<byte>.Shared));
                    switch (frame)
                    {
                        case HeadersFrame h:
                            HandleHeaders(h);
                            break;

                        case ContinuationFrame c:
                            HandleContinuation(c);
                            break;

                        case DataFrame d:
                            HandleData(d);
                            break;
                    }

                    Pull(stage.Inlet);
                });

                SetHandler(stage.Outlet, () => { Pull(stage.Inlet); });
            }

            private void HandleHeaders(HeadersFrame frame)
            {
                var state = _streams[frame.StreamId];

                state.AppendHeader(frame.HeaderBlockFragment.Span);

                if (!frame.EndHeaders)
                {
                    return;
                }

                DecodeHeaders(frame.StreamId, frame.EndStream);
            }

            private void HandleContinuation(ContinuationFrame frame)
            {
                var state = _streams[frame.StreamId];

                state.AppendHeader(frame.HeaderBlockFragment.Span);

                if (frame.EndHeaders)
                {
                    DecodeHeaders(frame.StreamId, false);
                }
            }

            private void HandleData(DataFrame frame)
            {
                var state = _streams[frame.StreamId];

                state.AppendBody(frame.Data.Span);

                if (!frame.EndStream) return;
                var response = state.Response ?? new HttpResponseMessage();

                response.Content = new ByteArrayContent(state.BodyBuffer[..state.BodyLength].ToArray());

                Push(_stage.Outlet, response);

                state.Dispose();
                _streams.Remove(frame.StreamId);
            }

            private void DecodeHeaders(int streamId, bool endStream)
            {
                var state = _streams[streamId];

                var headers = _hpack.Decode(state.HeaderBuffer[..state.HeaderLength].Span);

                var response = new HttpResponseMessage();

                foreach (var h in headers)
                {
                    if (h.Name == ":status")
                    {
                        response.StatusCode =
                            (HttpStatusCode)
                            int.Parse(h.Value);
                    }
                    else if (!h.Name.StartsWith(':'))
                    {
                        response.Headers.TryAddWithoutValidation(h.Name, h.Value);
                    }
                }

                state.Response = response;

                if (!endStream) return;
                Push(_stage.Outlet, response);

                state.Dispose();
                _streams.Remove(streamId);
            }
        }
    }
}