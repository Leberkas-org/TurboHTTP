using System.Net;
using System.Text;
using Akka;
using Akka.Streams.Dsl;
using Servus.Akka.Transport;
using TurboHTTP.Features.Cookies;
using TurboHTTP.Protocol.Semantics;
using TurboHTTP.Streams;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Streams.Stages;

public sealed class EnginePipelineDescriptorSpec : EngineTestBase
{
    private static byte[] Ok200() =>
        "HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

    private static byte[] Response503() =>
        "HTTP/1.1 503 Service Unavailable\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

    private static byte[] Response301() =>
        "HTTP/1.1 301 Moved Permanently\r\nLocation: http://example.com/new\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

    private Flow<ITransportOutbound, ITransportInbound, NotUsed> NoOpH2Flow()
        => CreateFakeConnectionFlow(() => []);

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
        var fake = CreateFakeConnection(Ok200);
        var engine = new Engine();
        var transports = new TransportRegistry()
            .Register(new Version(1, 0), CreateFakeConnectionFlow(Ok200))
            .Register(new Version(1, 1), fake.AsFlow())
            .Register(new Version(2, 0), NoOpH2Flow())
            .Register(new Version(3, 0), NoOpH2Flow());
        var flow = engine.CreateFlow(transports, PipelineDescriptor.Empty);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version11
        };

        await RunSingleAsync(flow, request);

        var rawBuilder = new StringBuilder();
        foreach (var outbound in fake.ReceivedOutbound)
        {
            if (outbound is TransportData { Buffer: var buf })
            {
                rawBuilder.Append(Encoding.Latin1.GetString(buf.Span));
            }
        }

        Assert.DoesNotContain("Cookie:", rawBuilder.ToString());
    }

    [Fact(Timeout = 10_000)]
    public async Task EnginePipelineDescriptor_should_pass_through_503_as_final_when_retry_policy_is_null()
    {
        var engine = new Engine();
        var transports = new TransportRegistry()
            .Register(new Version(1, 0), CreateFakeConnectionFlow(Response503))
            .Register(new Version(1, 1), CreateFakeConnectionFlow(Response503))
            .Register(new Version(2, 0), NoOpH2Flow())
            .Register(new Version(3, 0), NoOpH2Flow());
        var flow = engine.CreateFlow(transports, PipelineDescriptor.Empty);

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
        var transports = new TransportRegistry()
            .Register(new Version(1, 0), CreateFakeConnectionFlow(Response301))
            .Register(new Version(1, 1), CreateFakeConnectionFlow(Response301))
            .Register(new Version(2, 0), NoOpH2Flow())
            .Register(new Version(3, 0), NoOpH2Flow());
        var flow = engine.CreateFlow(transports, PipelineDescriptor.Empty);

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

        var fake = CreateFakeConnection(Ok200);
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
        var transports = new TransportRegistry()
            .Register(new Version(1, 0), CreateFakeConnectionFlow(Ok200))
            .Register(new Version(1, 1), fake.AsFlow())
            .Register(new Version(2, 0), NoOpH2Flow())
            .Register(new Version(3, 0), NoOpH2Flow());
        var flow = engine.CreateFlow(transports, descriptor);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version11
        };

        await RunSingleAsync(flow, request);

        var rawBuilder = new StringBuilder();
        foreach (var outbound in fake.ReceivedOutbound)
        {
            if (outbound is TransportData { Buffer: var buf })
            {
                rawBuilder.Append(Encoding.Latin1.GetString(buf.Span));
            }
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
        var transports = new TransportRegistry()
            .Register(new Version(1, 0), CreateFakeConnectionFlow(Ok200))
            .Register(new Version(1, 1), CreateFakeConnectionFlow(StatefulFactory))
            .Register(new Version(2, 0), NoOpH2Flow())
            .Register(new Version(3, 0), NoOpH2Flow());
        var flow = engine.CreateFlow(transports, descriptor);

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
        var transports = new TransportRegistry()
            .Register(new Version(1, 0), CreateFakeConnectionFlow(Ok200))
            .Register(new Version(1, 1), CreateFakeConnectionFlow(StatefulFactory))
            .Register(new Version(2, 0), NoOpH2Flow())
            .Register(new Version(3, 0), NoOpH2Flow());
        var flow = engine.CreateFlow(transports, descriptor);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version11
        };

        var response = await RunSingleAsync(flow, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, callCount);
    }
}