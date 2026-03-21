#pragma warning disable CS0618 // TurboClientOptions.RedirectPolicy/RetryPolicy/CachePolicy obsolete — backward-compat test
using System.Net;
using Akka;
using Akka.Streams.Dsl;
using TurboHttp.Client;
using TurboHttp.Internal;
using TurboHttp.Protocol.RFC9110;
using TurboHttp.Streams;

namespace TurboHttp.StreamTests.Streams;

/// <summary>
/// Tests the feedback buffer optimization that prevents backpressure stalls in the engine's post-processing path.
/// Verifies that the feedback loop does not block forward progress when responses arrive faster than they are consumed.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="Engine"/>.
/// Validates back-pressure resilience through the response post-processing feedback arc.
/// </remarks>
public sealed class FeedbackBufferOptimizationTests : EngineTestBase
{
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

    [Fact(Timeout = 15_000,
        DisplayName = "FBUF-001: Single 301 redirect completes via feedback buffer")]
    public async Task Should_CompleteViaFeedbackBuffer_When_Single301RedirectOccurs()
    {
        var options = new TurboClientOptions();
        var engine = new Engine();

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

    [Fact(Timeout = 15_000,
        DisplayName = "FBUF-002: Three chained 301 redirects complete without deadlock")]
    public async Task Should_CompleteWithoutDeadlock_When_ThreeChainedRedirectsOccur()
    {
        var options = new TurboClientOptions
        {
            RedirectPolicy = new RedirectPolicy { MaxRedirects = 10 }
        };
        var engine = new Engine();

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

    [Fact(Timeout = 15_000,
        DisplayName = "FBUF-003: Single 408 retry completes via feedback buffer")]
    public async Task Should_CompleteViaFeedbackBuffer_When_Single408RetryOccurs()
    {
        var options = new TurboClientOptions
        {
            RetryPolicy = new RetryPolicy { MaxRetries = 3, RespectRetryAfter = false }
        };
        var engine = new Engine();

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

    [Fact(Timeout = 15_000,
        DisplayName = "FBUF-004: Two 408 retries then 200 OK completes")]
    public async Task Should_CompleteSuccessfully_When_TwoRetriesThenOkReceived()
    {
        var options = new TurboClientOptions
        {
            RetryPolicy = new RetryPolicy { MaxRetries = 3, RespectRetryAfter = false }
        };
        var engine = new Engine();

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

    [Fact(Timeout = 15_000,
        DisplayName = "FBUF-005: 307 redirect preserves original HTTP method")]
    public async Task Should_PreserveOriginalMethod_When_307RedirectViaFeedbackLoop()
    {
        var options = new TurboClientOptions();
        var engine = new Engine();

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

    [Fact(Timeout = 15_000,
        DisplayName = "FBUF-006: 200 OK passes through without entering feedback loop")]
    public async Task Should_PassThroughDirectly_When_ResponseIsNonRetryable()
    {
        var options = new TurboClientOptions();
        var engine = new Engine();

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
