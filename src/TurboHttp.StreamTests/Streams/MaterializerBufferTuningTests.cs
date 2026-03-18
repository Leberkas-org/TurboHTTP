using System.Net;
using Akka;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.TestKit.Xunit2;
using TurboHttp.Client;
using TurboHttp.IO.Stages;
using TurboHttp.Protocol.RFC9113;
using TurboHttp.Streams;

namespace TurboHttp.StreamTests.Streams;

/// <summary>
/// Tests that materializer buffer tuning (TASK-12-003) works correctly:
/// - Global default reduced to 4/16
/// - Encoder/decoder protocol flows get 16/64 override
/// - Lightweight stages inherit the smaller global default
/// </summary>
public sealed class MaterializerBufferTuningTests : TestKit
{
    public MaterializerBufferTuningTests()
        : base(ActorSystem.Create("buffer-tuning-" + Guid.NewGuid()))
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
        IMaterializer materializer)
    {
        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(r => tcs.TrySetResult(r)), materializer);
        return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    // ── MBUF-001 ────────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "MBUF-001: Materializer with 4/16 input buffer — HTTP/1.1 pipeline completes")]
    public async Task MBUF_001_MaterializerWith4_16_Pipeline_Completes()
    {
        // Simulate the production materializer settings from TurboClientStreamManager
        var settings = ActorMaterializerSettings.Create(Sys)
            .WithInputBuffer(initialSize: 4, maxSize: 16);
        var materializer = Sys.Materializer(settings: settings);

        var engine = new Engine();
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
        Assert.Equal(HttpVersion.Version11, response.Version);
    }

    // ── MBUF-002 ────────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "MBUF-002: Materializer with 4/16 input buffer — HTTP/1.0 pipeline completes")]
    public async Task MBUF_002_MaterializerWith4_16_Http10_Completes()
    {
        var settings = ActorMaterializerSettings.Create(Sys)
            .WithInputBuffer(initialSize: 4, maxSize: 16);
        var materializer = Sys.Materializer(settings: settings);

        var engine = new Engine();
        var flow = engine.CreateFlow(
            () => Http10Flow(Ok10Response),
            () => Http11Flow(Ok11Response),
            () => Flow.FromGraph(new H2EngineFakeConnectionStage(new SettingsFrame([]).Serialize())),
            NoOpH2Flow);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version10
        };

        var response = await RunSingleAsync(flow, request, materializer);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpVersion.Version10, response.Version);
    }

    // ── MBUF-003 ────────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "MBUF-003: Protocol flows with high-throughput buffer — multiple requests complete")]
    public async Task MBUF_003_ProtocolFlows_HighThroughputBuffer_MultipleRequests()
    {
        // Use the reduced global default to verify the protocol flow override (16/64) is sufficient
        var settings = ActorMaterializerSettings.Create(Sys)
            .WithInputBuffer(initialSize: 4, maxSize: 16);
        var materializer = Sys.Materializer(settings: settings);

        var engine = new Engine();
        var flow = engine.CreateFlow(
            () => Http10Flow(Ok10Response),
            () => Http11Flow(Ok11Response),
            () => Flow.FromGraph(new H2EngineFakeConnectionStage(new SettingsFrame([]).Serialize())),
            NoOpH2Flow);

        // Send multiple requests to exercise buffering
        for (var i = 0; i < 5; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"http://example.com/path/{i}")
            {
                Version = HttpVersion.Version11
            };

            var response = await RunSingleAsync(flow, request, materializer);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    // ── MBUF-004 ────────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "MBUF-004: Lightweight stages inherit global default — pipeline with cookies completes")]
    public async Task MBUF_004_LightweightStages_InheritGlobalDefault()
    {
        // Enable cookies so CookieInjectionStage and CookieStorageStage are in the pipeline.
        // These lightweight stages should work fine with the reduced 4/16 global default.
        var settings = ActorMaterializerSettings.Create(Sys)
            .WithInputBuffer(initialSize: 4, maxSize: 16);
        var materializer = Sys.Materializer(settings: settings);

        var options = new TurboClientOptions();
        var engine = new Engine();
        var flow = engine.CreateFlow(
            () => Http10Flow(Ok10Response),
            () => Http11Flow(Ok11Response),
            () => Flow.FromGraph(new H2EngineFakeConnectionStage(new SettingsFrame([]).Serialize())),
            NoOpH2Flow,
            options);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/cookie-test")
        {
            Version = HttpVersion.Version11
        };

        var response = await RunSingleAsync(flow, request, materializer);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── MBUF-005 ────────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "MBUF-005: Engine flow with InputBuffer attribute applied at protocol level")]
    public async Task MBUF_005_EngineFlow_WithInputBufferAttribute_AtProtocolLevel()
    {
        // Verify that applying .WithAttributes at protocol flow level doesn't break the pipeline.
        // The Engine.BuildEngineCoreGraph applies InputBuffer(16,64) to each protocol flow.
        // This test verifies the pipeline still works correctly end-to-end.
        var settings = ActorMaterializerSettings.Create(Sys)
            .WithInputBuffer(initialSize: 4, maxSize: 16);
        var materializer = Sys.Materializer(settings: settings);

        var engine = new Engine();
        var flow = engine.CreateFlow(
            () => Http10Flow(Ok10Response),
            () => Http11Flow(Ok11Response),
            () => Flow.FromGraph(new H2EngineFakeConnectionStage(new SettingsFrame([]).Serialize())),
            NoOpH2Flow);

        // GET request through the protocol flow with tuned buffer attributes
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/buffer-test")
        {
            Version = HttpVersion.Version11
        };

        var response = await RunSingleAsync(flow, request, materializer);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── MBUF-006 ────────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "MBUF-006: Default materializer (16/16) still works — backward compatibility")]
    public async Task MBUF_006_DefaultMaterializer_StillWorks()
    {
        // Verify that using the default materializer (no custom settings) still works.
        // This ensures backward compatibility for tests and external consumers.
        var materializer = Sys.Materializer();

        var engine = new Engine();
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
}
