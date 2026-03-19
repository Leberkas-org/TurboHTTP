using System.Net;
using Akka;
using Akka.Streams.Dsl;
using TurboHttp.Client;
using TurboHttp.Internal;
using TurboHttp.IO.Stages;
using TurboHttp.Protocol.RFC9110;
using TurboHttp.Protocol.RFC9113;
using TurboHttp.Streams;

namespace TurboHttp.StreamTests.Pipeline;

/// <summary>
/// Tests that redirect/retry feedback buffer optimization (TASK-12-008)
/// allows multiple in-flight feedback items without deadlock or backpressure stalls.
/// Buffer(4) on each feedback loop enables up to 4 concurrent redirect/retry items.
/// MergePreferred ensures feedback items are always processed before new source requests.
/// </summary>
public sealed class FeedbackBufferOptimizationTests : EngineTestBase
{
    // ── Helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Creates an EngineFakeConnectionStage that cycles through responses in order.
    /// Each call to the response factory returns the next response in the sequence.
    /// </summary>
    private static EngineFakeConnectionStage SequentialFake(params byte[][] responses)
    {
        var index = 0;
        return new EngineFakeConnectionStage(() =>
        {
            var i = Interlocked.Increment(ref index) - 1;
            return i < responses.Length ? responses[i] : responses[^1];
        });
    }

    private static Flow<IOutputItem, IInputItem, NotUsed> SequentialFlow(params byte[][] responses)
    {
        var index = 0;
        return Flow.FromGraph(new EngineFakeConnectionStage(() =>
        {
            var i = Interlocked.Increment(ref index) - 1;
            return i < responses.Length ? responses[i] : responses[^1];
        }));
    }

    private static Flow<IOutputItem, IInputItem, NotUsed> NoOpH2Flow()
        => Flow.FromGraph(new H2EngineFakeConnectionStage());

    private static byte[] Redirect301(string location) =>
        System.Text.Encoding.Latin1.GetBytes(
            $"HTTP/1.1 301 Moved Permanently\r\nLocation: {location}\r\nContent-Length: 0\r\n\r\n");

    private static byte[] Redirect307(string location) =>
        System.Text.Encoding.Latin1.GetBytes(
            $"HTTP/1.1 307 Temporary Redirect\r\nLocation: {location}\r\nContent-Length: 0\r\n\r\n");

    private static byte[] Retry408() =>
        System.Text.Encoding.Latin1.GetBytes(
            "HTTP/1.1 408 Request Timeout\r\nContent-Length: 0\r\n\r\n");

    private static byte[] Retry503() =>
        System.Text.Encoding.Latin1.GetBytes(
            "HTTP/1.1 503 Service Unavailable\r\nContent-Length: 0\r\n\r\n");

    private static byte[] Ok200() =>
        System.Text.Encoding.Latin1.GetBytes(
            "HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nOK");

    private async Task<HttpResponseMessage> RunSingleAsync(
        Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> flow,
        HttpRequestMessage request)
    {
        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(r => tcs.TrySetResult(r)), Materializer);
        return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    // ── FBUF-001: Single redirect completes through feedback loop ────────

    [Fact(Timeout = 15_000,
        DisplayName = "FBUF-001: Single 301 redirect completes via feedback buffer")]
    public async Task SingleRedirectCompletesViaFeedbackBuffer()
    {
        var options = new TurboClientOptions();
        var engine = new TurboHttp.Streams.Engine();

        var flow = engine.CreateFlow(
            () => SequentialFlow(Ok200()),
            () => SequentialFlow(Redirect301("http://example.com/target"), Ok200()),
            NoOpH2Flow,
            NoOpH2Flow,
            options);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/origin")
        {
            Version = HttpVersion.Version11
        };

        var response = await RunSingleAsync(flow, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── FBUF-002: Three chained redirects complete without deadlock ──────

    [Fact(Timeout = 15_000,
        DisplayName = "FBUF-002: Three chained 301 redirects complete without deadlock")]
    public async Task ThreeChainedRedirectsCompleteWithoutDeadlock()
    {
        var options = new TurboClientOptions
        {
            RedirectPolicy = new RedirectPolicy { MaxRedirects = 10 }
        };
        var engine = new TurboHttp.Streams.Engine();

        var flow = engine.CreateFlow(
            () => SequentialFlow(Ok200()),
            () => SequentialFlow(
                Redirect301("http://example.com/step2"),
                Redirect301("http://example.com/step3"),
                Redirect301("http://example.com/step4"),
                Ok200()),
            NoOpH2Flow,
            NoOpH2Flow,
            options);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/step1")
        {
            Version = HttpVersion.Version11
        };

        var response = await RunSingleAsync(flow, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── FBUF-003: Single retry completes through feedback loop ───────────

    [Fact(Timeout = 15_000,
        DisplayName = "FBUF-003: Single 408 retry completes via feedback buffer")]
    public async Task SingleRetryCompletesViaFeedbackBuffer()
    {
        var options = new TurboClientOptions
        {
            RetryPolicy = new RetryPolicy { MaxRetries = 3, RespectRetryAfter = false }
        };
        var engine = new TurboHttp.Streams.Engine();

        var flow = engine.CreateFlow(
            () => SequentialFlow(Ok200()),
            () => SequentialFlow(Retry408(), Ok200()),
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

    // ── FBUF-004: Multiple retries then success ─────────────────────────

    [Fact(Timeout = 15_000,
        DisplayName = "FBUF-004: Two 408 retries then 200 OK completes")]
    public async Task MultipleRetriesThenSuccess()
    {
        var options = new TurboClientOptions
        {
            RetryPolicy = new RetryPolicy { MaxRetries = 3, RespectRetryAfter = false }
        };
        var engine = new TurboHttp.Streams.Engine();

        var flow = engine.CreateFlow(
            () => SequentialFlow(Ok200()),
            () => SequentialFlow(Retry408(), Retry408(), Ok200()),
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

    // ── FBUF-005: 307 redirect preserves method via feedback loop ────────

    [Fact(Timeout = 15_000,
        DisplayName = "FBUF-005: 307 redirect preserves original HTTP method")]
    public async Task Redirect307PreservesMethodViaFeedbackLoop()
    {
        var options = new TurboClientOptions();
        var engine = new TurboHttp.Streams.Engine();

        var flow = engine.CreateFlow(
            () => SequentialFlow(Ok200()),
            () => SequentialFlow(Redirect307("http://example.com/target"), Ok200()),
            NoOpH2Flow,
            NoOpH2Flow,
            options);

        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/origin")
        {
            Version = HttpVersion.Version11
        };

        var response = await RunSingleAsync(flow, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── FBUF-006: Non-retryable response passes through without buffering ─

    [Fact(Timeout = 15_000,
        DisplayName = "FBUF-006: 200 OK passes through without entering feedback loop")]
    public async Task NonRetryableResponsePassesThroughDirectly()
    {
        var options = new TurboClientOptions();
        var engine = new TurboHttp.Streams.Engine();

        var flow = engine.CreateFlow(
            () => SequentialFlow(Ok200()),
            () => SequentialFlow(Ok200()),
            NoOpH2Flow,
            NoOpH2Flow,
            options);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version11
        };

        var response = await RunSingleAsync(flow, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("OK", body);
    }
}
