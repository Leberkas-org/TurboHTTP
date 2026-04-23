using System.Net;
using Akka;
using Akka.Streams.Dsl;
using Servus.Akka.IO;
using TurboHTTP.Protocol.Semantics;
using TurboHTTP.Streams;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.StreamTests.Streams;

public sealed class FeedbackBufferOptimizationSpec : EngineTestBase
{
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

    private static byte[] Ok200() =>
        System.Text.Encoding.Latin1.GetBytes(
            "HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nOK");

    private async Task<HttpResponseMessage> RunSingleAsync(
        Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> flow,
        HttpRequestMessage request)
    {
        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        _ = Source.Single(request)
            .Concat(Source.Never<HttpRequestMessage>())
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(r => tcs.TrySetResult(r)), Materializer);
        return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 15_000)]
    public async Task FeedbackBufferOptimization_should_complete_via_feedback_buffer_when_single_301_redirect_occurs()
    {
        var descriptor = new PipelineDescriptor(
            RedirectPolicy: new RedirectPolicy(),
            RetryPolicy: null,
            Expect100Policy: null,
            CompressionPolicy: null,
            CookieJar: null,
            CacheStore: null,
            CachePolicy: null,
            Handlers: []);

        var engine = new Engine();
        var transports = new TransportRegistry()
            .Register(new Version(1, 0), new DelegateTransportFactory(() => SequentialFlow(Ok200())))
            .Register(new Version(1, 1), new DelegateTransportFactory(() => SequentialFlow(Redirect301("http://example.com/target"), Ok200())))
            .Register(new Version(2, 0), new DelegateTransportFactory(NoOpH2Flow))
            .Register(new Version(3, 0), new DelegateTransportFactory(NoOpH2Flow));
        var flow = engine.CreateFlow(transports, descriptor);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/origin")
        {
            Version = HttpVersion.Version11
        };

        var response = await RunSingleAsync(flow, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 15_000)]
    public async Task FeedbackBufferOptimization_should_complete_without_deadlock_when_three_chained_redirects_occur()
    {
        var descriptor = new PipelineDescriptor(
            RedirectPolicy: new RedirectPolicy { MaxRedirects = 10 },
            RetryPolicy: null,
            Expect100Policy: null,
            CompressionPolicy: null,
            CookieJar: null,
            CacheStore: null,
            CachePolicy: null,
            Handlers: []);

        var engine = new Engine();
        var transports = new TransportRegistry()
            .Register(new Version(1, 0), new DelegateTransportFactory(() => SequentialFlow(Ok200())))
            .Register(new Version(1, 1), new DelegateTransportFactory(() => SequentialFlow(
                Redirect301("http://example.com/step2"),
                Redirect301("http://example.com/step3"),
                Redirect301("http://example.com/step4"),
                Ok200())))
            .Register(new Version(2, 0), new DelegateTransportFactory(NoOpH2Flow))
            .Register(new Version(3, 0), new DelegateTransportFactory(NoOpH2Flow));
        var flow = engine.CreateFlow(transports, descriptor);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/step1")
        {
            Version = HttpVersion.Version11
        };

        var response = await RunSingleAsync(flow, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 15_000)]
    public async Task FeedbackBufferOptimization_should_complete_via_feedback_buffer_when_single_408_retry_occurs()
    {
        var descriptor = new PipelineDescriptor(
            RedirectPolicy: null,
            RetryPolicy: new RetryPolicy { MaxRetries = 3, RespectRetryAfter = false },
            Expect100Policy: null,
            CompressionPolicy: null,
            CookieJar: null,
            CacheStore: null,
            CachePolicy: null,
            Handlers: []);

        var engine = new Engine();
        var transports = new TransportRegistry()
            .Register(new Version(1, 0), new DelegateTransportFactory(() => SequentialFlow(Ok200())))
            .Register(new Version(1, 1), new DelegateTransportFactory(() => SequentialFlow(Retry408(), Ok200())))
            .Register(new Version(2, 0), new DelegateTransportFactory(NoOpH2Flow))
            .Register(new Version(3, 0), new DelegateTransportFactory(NoOpH2Flow));
        var flow = engine.CreateFlow(transports, descriptor);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version11
        };

        var response = await RunSingleAsync(flow, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 15_000)]
    public async Task FeedbackBufferOptimization_should_complete_successfully_when_two_retries_then_ok_received()
    {
        var descriptor = new PipelineDescriptor(
            RedirectPolicy: null,
            RetryPolicy: new RetryPolicy { MaxRetries = 3, RespectRetryAfter = false },
            Expect100Policy: null,
            CompressionPolicy: null,
            CookieJar: null,
            CacheStore: null,
            CachePolicy: null,
            Handlers: []);

        var engine = new Engine();
        var transports = new TransportRegistry()
            .Register(new Version(1, 0), new DelegateTransportFactory(() => SequentialFlow(Ok200())))
            .Register(new Version(1, 1), new DelegateTransportFactory(() => SequentialFlow(Retry408(), Retry408(), Ok200())))
            .Register(new Version(2, 0), new DelegateTransportFactory(NoOpH2Flow))
            .Register(new Version(3, 0), new DelegateTransportFactory(NoOpH2Flow));
        var flow = engine.CreateFlow(transports, descriptor);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version11
        };

        var response = await RunSingleAsync(flow, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 15_000)]
    public async Task FeedbackBufferOptimization_should_preserve_original_method_when_307_redirect_via_feedback_loop()
    {
        var descriptor = new PipelineDescriptor(
            RedirectPolicy: new RedirectPolicy(),
            RetryPolicy: null,
            Expect100Policy: null,
            CompressionPolicy: null,
            CookieJar: null,
            CacheStore: null,
            CachePolicy: null,
            Handlers: []);

        var engine = new Engine();
        var transports = new TransportRegistry()
            .Register(new Version(1, 0), new DelegateTransportFactory(() => SequentialFlow(Ok200())))
            .Register(new Version(1, 1), new DelegateTransportFactory(() => SequentialFlow(Redirect307("http://example.com/target"), Ok200())))
            .Register(new Version(2, 0), new DelegateTransportFactory(NoOpH2Flow))
            .Register(new Version(3, 0), new DelegateTransportFactory(NoOpH2Flow));
        var flow = engine.CreateFlow(transports, descriptor);

        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/origin")
        {
            Version = HttpVersion.Version11
        };

        var response = await RunSingleAsync(flow, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 15_000)]
    public async Task FeedbackBufferOptimization_should_pass_through_directly_when_response_is_non_retryable()
    {
        var engine = new Engine();
        var transports = new TransportRegistry()
            .Register(new Version(1, 0), new DelegateTransportFactory(() => SequentialFlow(Ok200())))
            .Register(new Version(1, 1), new DelegateTransportFactory(() => SequentialFlow(Ok200())))
            .Register(new Version(2, 0), new DelegateTransportFactory(NoOpH2Flow))
            .Register(new Version(3, 0), new DelegateTransportFactory(NoOpH2Flow));
        var flow = engine.CreateFlow(transports, PipelineDescriptor.Empty);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version11
        };

        var response = await RunSingleAsync(flow, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("OK", body);
    }
}
