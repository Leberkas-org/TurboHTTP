using System;
using System.Buffers;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.Stage;
using BenchmarkDotNet.Attributes;
using TurboHttp.Internal;
using TurboHttp.IO.Stages;
using TurboHttp.Protocol.RFC7541;
using TurboHttp.Protocol.RFC9113;
using TurboHttp.Streams;

namespace TurboHttp.Benchmarks;

// ──────────────────────────────────────────────────────────────────────────────
// Loopback transport stages (no real TCP — isolates stream/stage overhead)
// ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Loopback stage for HTTP/1.x: accepts encoded request bytes, immediately
/// returns a pre-baked 200 OK response. Stays alive for repeated iterations.
/// </summary>
internal sealed class LoopbackHttp1Stage : GraphStage<FlowShape<IOutputItem, IInputItem>>
{
    private readonly Func<byte[]> _responseFactory;

    public Inlet<IOutputItem> In { get; } = new("loopback-h1.in");
    public Outlet<IInputItem> Out { get; } = new("loopback-h1.out");
    public override FlowShape<IOutputItem, IInputItem> Shape { get; }

    public LoopbackHttp1Stage(Func<byte[]> responseFactory)
    {
        _responseFactory = responseFactory;
        Shape = new FlowShape<IOutputItem, IInputItem>(In, Out);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly LoopbackHttp1Stage _stage;
        private readonly Queue<(IMemoryOwner<byte> Owner, int Len)> _buffer = new();
        private bool _downstreamWaiting;

        public Logic(LoopbackHttp1Stage stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage.In,
                onPush: () =>
                {
                    var item = Grab(stage.In);
                    if (item is DataItem(var owner, _))
                    {
                        owner.Dispose();
                    }

                    var responseBytes = _stage._responseFactory();
                    IMemoryOwner<byte> responseOwner = new BenchmarkMemoryOwner(responseBytes);

                    if (_downstreamWaiting)
                    {
                        _downstreamWaiting = false;
                        Push(stage.Out,
                            new DataItem(responseOwner, responseBytes.Length) { Key = RequestEndpoint.Default });
                    }
                    else
                    {
                        _buffer.Enqueue((responseOwner, responseBytes.Length));
                    }

                    Pull(stage.In);
                },
                onUpstreamFinish: CompleteStage,
                onUpstreamFailure: FailStage);

            SetHandler(stage.Out,
                onPull: () =>
                {
                    if (_buffer.TryDequeue(out var chunk))
                    {
                        Push(stage.Out,
                            new DataItem(chunk.Owner, chunk.Len) { Key = RequestEndpoint.Default });
                    }
                    else
                    {
                        _downstreamWaiting = true;
                    }
                },
                onDownstreamFinish: _ => CompleteStage());
        }

        public override void PreStart() => Pull(_stage.In);
    }
}

/// <summary>
/// Loopback stage for HTTP/2: handles the connection lifecycle (SETTINGS
/// handshake) and dynamically generates HEADERS responses matching each
/// incoming request's stream ID. Stays alive for repeated iterations.
/// </summary>
internal sealed class LoopbackHttp20Stage : GraphStage<FlowShape<IOutputItem, IInputItem>>
{
    // Pre-encoded server SETTINGS frame (empty — no parameter overrides)
    private static readonly byte[] _serverSettings = new SettingsFrame([]).Serialize();

    // Pre-encoded HPACK block for ":status: 200"
    private static readonly ReadOnlyMemory<byte> _statusOkBlock =
        new HpackEncoder(useHuffman: false).Encode([(":status", "200")]);

    private static ReadOnlySpan<byte> H2Preface => "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8;

    public Inlet<IOutputItem> In { get; } = new("loopback-h2.in");
    public Outlet<IInputItem> Out { get; } = new("loopback-h2.out");
    public override FlowShape<IOutputItem, IInputItem> Shape { get; }

    public LoopbackHttp20Stage()
    {
        Shape = new FlowShape<IOutputItem, IInputItem>(In, Out);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly LoopbackHttp20Stage _stage;
        private readonly Queue<IInputItem> _pending = new();
        private bool _settingsSent;
        private bool _downstreamWaiting;

        public Logic(LoopbackHttp20Stage stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage.In,
                onPush: () =>
                {
                    var item = Grab(stage.In);

                    if (item is ConnectItem && !_settingsSent)
                    {
                        _settingsSent = true;
                        Enqueue(new DataItem(
                            new BenchmarkMemoryOwner(_serverSettings),
                            _serverSettings.Length) { Key = RequestEndpoint.Default });
                    }
                    else if (item is DataItem(var owner, var length))
                    {
                        var span = owner.Memory.Span[..length];

                        // Strip connection preface if present on the first DataItem
                        if (length >= 24 && span[..24].SequenceEqual(H2Preface))
                        {
                            span = span[24..];
                        }

                        owner.Dispose();

                        // Parse frame headers, respond to each HEADERS frame
                        RespondToRequestFrames(span);
                    }

                    Pull(stage.In);
                },
                onUpstreamFinish: CompleteStage,
                onUpstreamFailure: FailStage);

            SetHandler(stage.Out,
                onPull: () =>
                {
                    if (_pending.TryDequeue(out var queued))
                    {
                        Push(stage.Out, queued);
                    }
                    else
                    {
                        _downstreamWaiting = true;
                    }
                },
                onDownstreamFinish: _ => CompleteStage());
        }

        private void Enqueue(IInputItem item)
        {
            if (_downstreamWaiting)
            {
                _downstreamWaiting = false;
                Push(_stage.Out, item);
            }
            else
            {
                _pending.Enqueue(item);
            }
        }

        /// <summary>
        /// Walk the raw bytes, find every HEADERS frame (type=0x01) for a
        /// client stream (odd, non-zero stream ID) and push a 200 response.
        /// </summary>
        private void RespondToRequestFrames(ReadOnlySpan<byte> bytes)
        {
            while (bytes.Length >= 9)
            {
                var payloadLen = (bytes[0] << 16) | (bytes[1] << 8) | bytes[2];
                var frameType = bytes[3];
                var streamId = (int)(((uint)bytes[5] & 0x7Fu) << 24
                                     | ((uint)bytes[6]) << 16
                                     | ((uint)bytes[7]) << 8
                                     | (uint)bytes[8]);
                var totalFrameLen = 9 + payloadLen;

                if (bytes.Length < totalFrameLen)
                {
                    break;
                }

                // HEADERS frame (0x01) on an odd non-zero stream = client request
                if (frameType == (byte)FrameType.Headers && streamId > 0 && (streamId & 1) == 1)
                {
                    var responseFrame = new HeadersFrame(
                        streamId: streamId,
                        headerBlock: _statusOkBlock,
                        endStream: true,
                        endHeaders: true).Serialize();

                    Enqueue(new DataItem(
                        new BenchmarkMemoryOwner(responseFrame),
                        responseFrame.Length) { Key = RequestEndpoint.Default });
                }

                bytes = bytes[totalFrameLen..];
            }
        }

        public override void PreStart() => Pull(_stage.In);
    }
}

/// <summary>
/// Lightweight <see cref="IMemoryOwner{T}"/> wrapper used inside the loopback
/// stages — wraps a byte[] without ArrayPool ownership.
/// </summary>
internal sealed class BenchmarkMemoryOwner(byte[] data) : IMemoryOwner<byte>
{
    public Memory<byte> Memory { get; } = data;
    public void Dispose() { }
}

// ──────────────────────────────────────────────────────────────────────────────
// Benchmark configuration
// ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// BenchmarkDotNet config for the engine pipeline benchmarks.
/// Adds p50/p99 latency columns on top of the shared <see cref="MicroBenchmarkConfig"/>.
/// </summary>
public class EngineBenchmarkConfig : MicroBenchmarkConfig
{
    public EngineBenchmarkConfig()
    {
        AddColumn(BenchmarkDotNet.Columns.StatisticColumn.P50);
        AddColumn(BenchmarkDotNet.Columns.StatisticColumn.P95);
        AddColumn(BenchmarkDotNet.Columns.StatisticColumn.P100);
    }
}

// ──────────────────────────────────────────────────────────────────────────────
// Benchmarks
// ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Measures request throughput and latency through the full Engine pipeline
/// (encode → decode → correlate) using a loopback transport (no real TCP).
///
/// Metrics captured:
///   • Mean / p50 / p99 latency  (BenchmarkDotNet statistics columns)
///   • Allocations bytes/op      (MemoryDiagnoser)
///   • Req/sec                   (custom RequestsPerSecondColumn)
/// </summary>
[Config(typeof(EngineBenchmarkConfig))]
[SimpleJob(warmupCount: 3, targetCount: 5)]
public class EnginePipelineBenchmarks
{
    // ── Pre-baked response bytes ──────────────────────────────────────────────
    private static byte[] Http11OkResponse() =>
        "HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

    // ── Akka infrastructure ──────────────────────────────────────────────────
    private ActorSystem _actorSystem = null!;
    private IMaterializer _materializer = null!;

    // ── HTTP/1.1 pipeline ────────────────────────────────────────────────────
    private ISourceQueueWithComplete<HttpRequestMessage> _http11Queue = null!;
    private Channel<HttpResponseMessage> _http11Responses = null!;

    // ── HTTP/2 pipeline ──────────────────────────────────────────────────────
    private ISourceQueueWithComplete<HttpRequestMessage> _http20Queue = null!;
    private Channel<HttpResponseMessage> _http20Responses = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _actorSystem = ActorSystem.Create("engine-bench-" + Guid.NewGuid());
        _materializer = _actorSystem.Materializer();

        SetupHttp11Pipeline();
        SetupHttp20Pipeline();
    }

    private void SetupHttp11Pipeline()
    {
        _http11Responses = Channel.CreateUnbounded<HttpResponseMessage>();

        var engine = new Engine();
        var flow = engine.CreateFlow(
            http10Factory: () => Flow.FromGraph(new LoopbackHttp1Stage(Http11OkResponse)),
            http11Factory: () => Flow.FromGraph(new LoopbackHttp1Stage(Http11OkResponse)),
            http20Factory: () => Flow.FromGraph(new LoopbackHttp1Stage(Http11OkResponse)),
            http30Factory: () => Flow.FromGraph(new LoopbackHttp1Stage(Http11OkResponse)),
            options: null);

        (_http11Queue, _) = Source.Queue<HttpRequestMessage>(16, OverflowStrategy.Backpressure)
            .Via(flow)
            .ToMaterialized(
                Sink.ForEach<HttpResponseMessage>(r => _http11Responses.Writer.TryWrite(r)),
                Keep.Both)
            .Run(_materializer);
    }

    private void SetupHttp20Pipeline()
    {
        _http20Responses = Channel.CreateUnbounded<HttpResponseMessage>();

        var engine = new Engine();
        var flow = engine.CreateFlow(
            http10Factory: () => Flow.FromGraph(new LoopbackHttp1Stage(Http11OkResponse)),
            http11Factory: () => Flow.FromGraph(new LoopbackHttp1Stage(Http11OkResponse)),
            http20Factory: () => Flow.FromGraph(new LoopbackHttp20Stage()),
            http30Factory: () => Flow.FromGraph(new LoopbackHttp1Stage(Http11OkResponse)),
            options: null);

        (_http20Queue, _) = Source.Queue<HttpRequestMessage>(16, OverflowStrategy.Backpressure)
            .Via(flow)
            .ToMaterialized(
                Sink.ForEach<HttpResponseMessage>(r => _http20Responses.Writer.TryWrite(r)),
                Keep.Both)
            .Run(_materializer);
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _http11Queue.Complete();
        _http20Queue.Complete();
        _actorSystem.Terminate().GetAwaiter().GetResult();
    }

    // ── HTTP/1.1 benchmark ───────────────────────────────────────────────────

    /// <summary>
    /// Full HTTP/1.1 round-trip: enrich → encode → loopback → decode → correlate.
    /// </summary>
    [Benchmark(Description = "HTTP/1.1 full-engine round-trip (loopback)")]
    public async Task Http11_SingleRequest_RoundTrip()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version11
        };

        await _http11Queue.OfferAsync(request);
        await _http11Responses.Reader.ReadAsync();
    }

    // ── HTTP/2 benchmark ─────────────────────────────────────────────────────

    /// <summary>
    /// Full HTTP/2 round-trip: enrich → stream-ID alloc → encode → loopback → decode → correlate.
    /// </summary>
    [Benchmark(Description = "HTTP/2 full-engine round-trip (loopback)")]
    public async Task Http20_SingleRequest_RoundTrip()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version20
        };

        await _http20Queue.OfferAsync(request);
        await _http20Responses.Reader.ReadAsync();
    }
}
