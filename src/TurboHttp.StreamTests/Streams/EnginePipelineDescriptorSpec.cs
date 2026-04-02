using System.Net;
using System.Text;
using Akka;
using Akka.Streams.Dsl;
using TurboHttp.Internal;
using TurboHttp.Protocol.Cookies;
using TurboHttp.Protocol.Semantics;
using TurboHttp.Streams;

namespace TurboHttp.StreamTests.Streams;

/// <summary>
/// Tests that <see cref="PipelineDescriptor"/> controls which middleware stages are wired into the engine.
/// Verifies that null policies result in pass-through behaviour (FR-8) and that non-null policies
/// activate the corresponding stage.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="Engine"/> (internal CreateFlow overload with <see cref="PipelineDescriptor"/>).
/// </remarks>
public sealed class EnginePipelineDescriptorSpec : EngineTestBase
{
    private static byte[] Ok200() =>
        "HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

    private static byte[] Response503() =>
        "HTTP/1.1 503 Service Unavailable\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

    private static byte[] Response301() =>
        "HTTP/1.1 301 Moved Permanently\r\nLocation: http://example.com/new\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

    private static Flow<IOutputItem, IInputItem, NotUsed> NoOpH2Flow()
        => Flow.FromGraph(new H2EngineFakeConnectionStage());

    /// <summary>
    /// Runs a single request through the engine flow and returns the response.
    /// Uses <c>Source.Never</c> concat to keep the source alive during feedback loops
    /// (retry / redirect), so that <see cref="MergePreferred"/> can process re-injected
    /// requests even after the original source has emitted its one item.
    /// </summary>
    private async Task<HttpResponseMessage> RunSingleAsync(
        Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> flow,
        HttpRequestMessage request)
    {
        var tcs = new TaskCompletionSource<HttpResponseMessage>();

        _ = Source.Single(request)
            .Concat(Source.Never<HttpRequestMessage>())
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(r => tcs.TrySetResult(r)), Materializer);

        return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 10_000)]
    public async Task EnginePipelineDescriptor_should_not_inject_cookie_header_when_cookie_jar_is_null()
    {
        var fake = new EngineFakeConnectionStage(Ok200);
        var engine = new Engine();
        var flow = engine.CreateFlow(
            () => Flow.FromGraph(new EngineFakeConnectionStage(Ok200)),
            () => Flow.FromGraph(fake),
            NoOpH2Flow,
            NoOpH2Flow,
            PipelineDescriptor.Empty);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version11
        };

        await RunSingleAsync(flow, request);

        var rawBuilder = new StringBuilder();
        while (fake.OutboundChannel.Reader.TryRead(out var chunk))
        {
            rawBuilder.Append(Encoding.Latin1.GetString(chunk.Span));
        }

        Assert.DoesNotContain("Cookie:", rawBuilder.ToString());
    }

    [Fact(Timeout = 10_000)]
    public async Task EnginePipelineDescriptor_should_pass_through_503_as_final_when_retry_policy_is_null()
    {
        var engine = new Engine();
        var flow = engine.CreateFlow(
            () => Flow.FromGraph(new EngineFakeConnectionStage(Response503)),
            () => Flow.FromGraph(new EngineFakeConnectionStage(Response503)),
            NoOpH2Flow,
            NoOpH2Flow,
            PipelineDescriptor.Empty);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version11
        };

        var response = await RunSingleAsync(flow, request);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact(Timeout = 10_000)]
    public async Task EnginePipelineDescriptor_should_pass_through_301_as_final_when_redirect_policy_is_null()
    {
        var engine = new Engine();
        var flow = engine.CreateFlow(
            () => Flow.FromGraph(new EngineFakeConnectionStage(Response301)),
            () => Flow.FromGraph(new EngineFakeConnectionStage(Response301)),
            NoOpH2Flow,
            NoOpH2Flow,
            PipelineDescriptor.Empty);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version11
        };

        var response = await RunSingleAsync(flow, request);

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
    }

    [Fact(Timeout = 10_000)]
    public async Task EnginePipelineDescriptor_should_inject_cookie_header_when_cookie_jar_has_matching_cookie()
    {
        var cookieJar = new CookieJar();
        var seedRequest = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var seedResponse = new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = seedRequest };
        seedResponse.Headers.Add("Set-Cookie", "session=abc; Path=/; Domain=example.com");
        cookieJar.ProcessResponse(new Uri("http://example.com/"), seedResponse);

        var fake = new EngineFakeConnectionStage(Ok200);
        var descriptor = new PipelineDescriptor(
            RedirectPolicy: null,
            RetryPolicy: null,
            Expect100Policy: null,
            CompressionPolicy: null,
            CookieJar: cookieJar,
            CacheStore: null,
            CachePolicy: null,
            Handlers: []);

        var engine = new Engine();
        var flow = engine.CreateFlow(
            () => Flow.FromGraph(new EngineFakeConnectionStage(Ok200)),
            () => Flow.FromGraph(fake),
            NoOpH2Flow,
            NoOpH2Flow,
            descriptor);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version11
        };

        await RunSingleAsync(flow, request);

        var rawBuilder = new StringBuilder();
        while (fake.OutboundChannel.Reader.TryRead(out var chunk))
        {
            rawBuilder.Append(Encoding.Latin1.GetString(chunk.Span));
        }

        var rawText = rawBuilder.ToString();
        Assert.Contains("Cookie:", rawText);
        Assert.Contains("session=abc", rawText);
    }

    [Fact(Timeout = 10_000)]
    public async Task EnginePipelineDescriptor_should_retry_on_503_and_return_200_when_retry_policy_is_set()
    {
        var callCount = 0;
        byte[] StatefulFactory() => ++callCount == 1 ? Response503() : Ok200();

        var descriptor = new PipelineDescriptor(
            RedirectPolicy: null,
            RetryPolicy: new RetryPolicy(),
            Expect100Policy: null,
            CompressionPolicy: null,
            CookieJar: null,
            CacheStore: null,
            CachePolicy: null,
            Handlers: []);

        var engine = new Engine();
        var flow = engine.CreateFlow(
            () => Flow.FromGraph(new EngineFakeConnectionStage(Ok200)),
            () => Flow.FromGraph(new EngineFakeConnectionStage(StatefulFactory)),
            NoOpH2Flow,
            NoOpH2Flow,
            descriptor);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version11
        };

        var response = await RunSingleAsync(flow, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, callCount);
    }

    [Fact(Timeout = 10_000)]
    public async Task EnginePipelineDescriptor_should_follow_redirect_and_return_200_when_redirect_policy_is_set()
    {
        var callCount = 0;
        byte[] StatefulFactory() => ++callCount == 1 ? Response301() : Ok200();

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
        var flow = engine.CreateFlow(
            () => Flow.FromGraph(new EngineFakeConnectionStage(Ok200)),
            () => Flow.FromGraph(new EngineFakeConnectionStage(StatefulFactory)),
            NoOpH2Flow,
            NoOpH2Flow,
            descriptor);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version11
        };

        var response = await RunSingleAsync(flow, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, callCount);
    }
}
