using System.Net;
using Akka;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.TestKit.Xunit2;
using TurboHttp.Client;
using TurboHttp.Internal;
using TurboHttp.IO.Stages;
using TurboHttp.Protocol.RFC9113;
using TurboHttp.Streams;

namespace TurboHttp.StreamTests.Streams;

/// <summary>
/// Tests that async boundaries (TASK-12-004) are correctly placed in the Engine pipeline
/// to create three fused islands without breaking existing functionality.
/// </summary>
public sealed class AsyncBoundaryTests : TestKit
{
    public AsyncBoundaryTests()
        : base(ActorSystem.Create("async-boundary-" + Guid.NewGuid()))
    {
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static Flow<IOutputItem, IInputItem, NotUsed> Http11Flow(
        Func<byte[]> responseFactory)
        => Flow.FromGraph(new EngineFakeConnectionStage(responseFactory));

    private static Flow<IOutputItem, IInputItem, NotUsed> Http10Flow(
        Func<byte[]> responseFactory)
        => Flow.FromGraph(new EngineFakeConnectionStage(responseFactory));

    private static Flow<IOutputItem, IInputItem, NotUsed> NoOpH2Flow()
        => Flow.FromGraph(new H2EngineFakeConnectionStage());

    private static byte[] Ok11Response() =>
        "HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

    private static byte[] Ok10Response() =>
        "HTTP/1.0 200 OK\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

    private async Task<HttpResponseMessage> RunSingleAsync(
        Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> flow,
        HttpRequestMessage request,
        IMaterializer? materializer = null)
    {
        materializer ??= Sys.Materializer();
        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(r => tcs.TrySetResult(r)), materializer);
        return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    // ── ABND-001: HTTP/1.1 request completes with async boundaries ──────

    [Fact(Timeout = 15_000, DisplayName = "ABND-001: HTTP/1.1 request completes with async boundary islands")]
    public async Task ABND_001_Http11_CompletesWithAsyncBoundaries()
    {
        var engine = new TurboHttp.Streams.Engine();
        var flow = engine.CreateFlow(
            () => Http10Flow(Ok10Response),
            () => Http11Flow(Ok11Response),
            () => Flow.FromGraph(new H2EngineFakeConnectionStage(new SettingsFrame([]).Serialize())),
            NoOpH2Flow);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version11
        };

        var response = await RunSingleAsync(flow, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpVersion.Version11, response.Version);
    }

    // ── ABND-002: HTTP/1.0 request completes with async boundaries ──────

    [Fact(Timeout = 15_000, DisplayName = "ABND-002: HTTP/1.0 request completes with async boundary islands")]
    public async Task ABND_002_Http10_CompletesWithAsyncBoundaries()
    {
        var engine = new TurboHttp.Streams.Engine();
        var flow = engine.CreateFlow(
            () => Http10Flow(Ok10Response),
            () => Http11Flow(Ok11Response),
            () => Flow.FromGraph(new H2EngineFakeConnectionStage(new SettingsFrame([]).Serialize())),
            NoOpH2Flow);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version10
        };

        var response = await RunSingleAsync(flow, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpVersion.Version10, response.Version);
    }

    // ── ABND-003: Multiple sequential requests complete ──────────────────

    [Fact(Timeout = 15_000, DisplayName = "ABND-003: Multiple sequential requests complete with async boundaries")]
    public async Task ABND_003_MultipleRequests_CompleteWithAsyncBoundaries()
    {
        var engine = new TurboHttp.Streams.Engine();
        var flow = engine.CreateFlow(
            () => Http10Flow(Ok10Response),
            () => Http11Flow(Ok11Response),
            () => Flow.FromGraph(new H2EngineFakeConnectionStage(new SettingsFrame([]).Serialize())),
            NoOpH2Flow);

        for (var i = 0; i < 5; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"http://example.com/path/{i}")
            {
                Version = HttpVersion.Version11
            };

            var response = await RunSingleAsync(flow, request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    // ── ABND-004: Reduced materializer buffer with async boundaries ──────

    [Fact(Timeout = 15_000, DisplayName = "ABND-004: Reduced materializer buffer 4/16 works with async boundaries")]
    public async Task ABND_004_ReducedMaterializerBuffer_WorksWithAsyncBoundaries()
    {
        var settings = ActorMaterializerSettings.Create(Sys)
            .WithInputBuffer(initialSize: 4, maxSize: 16);
        var materializer = Sys.Materializer(settings: settings);

        var engine = new TurboHttp.Streams.Engine();
        var flow = engine.CreateFlow(
            () => Http10Flow(Ok10Response),
            () => Http11Flow(Ok11Response),
            () => Flow.FromGraph(new H2EngineFakeConnectionStage(new SettingsFrame([]).Serialize())),
            NoOpH2Flow);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version11
        };

        var response = await RunSingleAsync(flow, request, materializer);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── ABND-005: Full pipeline with TurboClientOptions ──────────────────

    [Fact(Timeout = 15_000, DisplayName = "ABND-005: Full pipeline with TurboClientOptions completes with async boundaries")]
    public async Task ABND_005_FullPipeline_WithOptions_CompletesWithAsyncBoundaries()
    {
        var options = new TurboClientOptions();
        var engine = new TurboHttp.Streams.Engine();
        var flow = engine.CreateFlow(
            () => Http10Flow(Ok10Response),
            () => Http11Flow(Ok11Response),
            NoOpH2Flow,
            NoOpH2Flow,
            options);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version11
        };

        var response = await RunSingleAsync(flow, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── ABND-006: Null options overload works with async boundaries ──────

    [Fact(Timeout = 15_000, DisplayName = "ABND-006: CreateFlow with null options works with async boundaries")]
    public async Task ABND_006_NullOptions_WorksWithAsyncBoundaries()
    {
        var engine = new TurboHttp.Streams.Engine();
        var flow = engine.CreateFlow(
            () => Http10Flow(Ok10Response),
            () => Http11Flow(Ok11Response),
            NoOpH2Flow,
            NoOpH2Flow,
            options: null);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version11
        };

        var response = await RunSingleAsync(flow, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
