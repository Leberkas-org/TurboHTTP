using System.IO.Compression;
using System.Net;
using Akka;
using Akka.Streams.Dsl;
using Servus.Akka.Transport;
using TurboHTTP.Protocol.Caching;
using TurboHTTP.Protocol.Cookies;
using TurboHTTP.Protocol.Semantics;
using TurboHTTP.Streams;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Streams.Stages;

public sealed class EngineBidiFlowCompositionSpec : EngineTestBase
{
    private static byte[] Ok200() =>
        "HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

    private static byte[] Response503() =>
        "HTTP/1.1 503 Service Unavailable\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

    private static byte[] Response301() =>
        "HTTP/1.1 301 Moved Permanently\r\nLocation: http://example.com/new\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

    private static byte[] Response200WithSetCookie() =>
        "HTTP/1.1 200 OK\r\nSet-Cookie: token=xyz; Domain=example.com; Path=/\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

    private static Flow<ITransportOutbound, ITransportInbound, NotUsed> NoOpH2Flow()
        => CreateFakeConnectionFlow(() => Array.Empty<byte>());

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

    private Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> BuildFlow(
        PipelineDescriptor descriptor,
        Func<byte[]>? http11ResponseFactory = null)
    {
        http11ResponseFactory ??= Ok200;
        var engine = new Engine();
        var transports = new TransportRegistry()
            .Register(new Version(1, 0), CreateFakeConnectionFlow(Ok200))
            .Register(new Version(1, 1), CreateFakeConnectionFlow(http11ResponseFactory))
            .Register(new Version(2, 0), NoOpH2Flow())
            .Register(new Version(3, 0), NoOpH2Flow());
        return engine.CreateFlow(transports, descriptor);
    }

    [Fact(Timeout = 10_000)]
    public async Task EngineBidiFlowComposition_should_return_200ok_when_empty_descriptor()
    {
        var flow = BuildFlow(PipelineDescriptor.Empty);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version11
        };

        var response = await RunSingleAsync(flow, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 10_000)]
    public async Task EngineBidiFlowComposition_should_pass_through_503_when_empty_descriptor()
    {
        var flow = BuildFlow(PipelineDescriptor.Empty, Response503);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version11
        };

        var response = await RunSingleAsync(flow, request);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact(Timeout = 10_000)]
    public async Task EngineBidiFlowComposition_should_pass_through_301_when_empty_descriptor()
    {
        var flow = BuildFlow(PipelineDescriptor.Empty, Response301);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version11
        };

        var response = await RunSingleAsync(flow, request);
        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
    }

    [Fact(Timeout = 10_000)]
    public async Task EngineBidiFlowComposition_should_still_deliver_response_when_automatic_decompression_disabled()
    {
        // Note: decompression also happens in the protocol-level decoders (Http11Decoder,
        // Http20StreamStage), so AutomaticDecompression=false only removes the BidiStage
        // from the feature chain — it does not prevent protocol-level decompression.
        // This test verifies the pipeline still works correctly with the flag disabled.
        var descriptor = new PipelineDescriptor(
            RedirectPolicy: null, RetryPolicy: null, Expect100Policy: null, CompressionPolicy: null, CookieJar: null,
            CacheStore: null, CachePolicy: null, Handlers: [],
            AutomaticDecompression: false);

        var flow = BuildFlow(descriptor);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version11
        };

        var response = await RunSingleAsync(flow, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 10_000)]
    public async Task EngineBidiFlowComposition_should_retry_on_503_when_only_retry_policy_is_set()
    {
        var callCount = 0;
        byte[] Factory() => ++callCount == 1 ? Response503() : Ok200();

        var descriptor = new PipelineDescriptor(
            RedirectPolicy: null, RetryPolicy: new RetryPolicy(), Expect100Policy: null, CompressionPolicy: null,
            CookieJar: null, CacheStore: null, CachePolicy: null, Handlers: []);

        var flow = BuildFlow(descriptor, Factory);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version11
        };

        var response = await RunSingleAsync(flow, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, callCount);
    }

    [Fact(Timeout = 15_000)]
    public async Task EngineBidiFlowComposition_should_follow_redirect_when_only_redirect_policy_is_set()
    {
        var callCount = 0;
        byte[] Factory() => ++callCount == 1 ? Response301() : Ok200();

        var descriptor = new PipelineDescriptor(
            RedirectPolicy: new RedirectPolicy(), RetryPolicy: null, Expect100Policy: null, CompressionPolicy: null,
            CookieJar: null, CacheStore: null, CachePolicy: null, Handlers: []);

        var flow = BuildFlow(descriptor, Factory);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/old")
        {
            Version = HttpVersion.Version11
        };

        var response = await RunSingleAsync(flow, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, callCount);
    }

    [Fact(Timeout = 10_000)]
    public async Task EngineBidiFlowComposition_should_store_cookie_from_response_when_only_cookie_jar_is_set()
    {
        var jar = new CookieJar();
        var descriptor = new PipelineDescriptor(
            RedirectPolicy: null, RetryPolicy: null, Expect100Policy: null, CompressionPolicy: null,
            CookieJar: jar, CacheStore: null, CachePolicy: null, Handlers: []);

        var flow = BuildFlow(descriptor, Response200WithSetCookie);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version11
        };

        await RunSingleAsync(flow, request);

        // Verify cookie was stored in the jar
        var nextRequest = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page");
        jar.AddCookiesToRequest(new Uri("http://example.com/page"), ref nextRequest);
        Assert.True(nextRequest.Headers.Contains("Cookie"));
        var cookieValue = string.Join("; ", nextRequest.Headers.GetValues("Cookie"));
        Assert.Contains("token=xyz", cookieValue);
    }

    [Fact(Timeout = 10_000)]
    public async Task EngineBidiFlowComposition_should_serve_cached_response_when_only_cache_store_is_set()
    {
        var store = new Cache();
        var callCount = 0;

        byte[] Factory()
        {
            callCount++;
            return
                "HTTP/1.1 200 OK\r\nCache-Control: max-age=3600\r\nDate: Thu, 21 Mar 2026 10:00:00 GMT\r\nContent-Length: 5\r\n\r\nhello"u8
                    .ToArray();
        }

        var descriptor = new PipelineDescriptor(
            RedirectPolicy: null, RetryPolicy: null, Expect100Policy: null, CompressionPolicy: null,
            CookieJar: null, CacheStore: store, CachePolicy: null, Handlers: []);

        var flow = BuildFlow(descriptor, Factory);

        // First request — cache miss, hits engine
        var request1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/data")
        {
            Version = HttpVersion.Version11
        };
        var response1 = await RunSingleAsync(flow, request1);
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        Assert.Equal(1, callCount);

        // Second request with a fresh flow (same store) — should be served from cache
        var flow2 = BuildFlow(descriptor, Factory);
        var request2 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/data")
        {
            Version = HttpVersion.Version11
        };
        var response2 = await RunSingleAsync(flow2, request2);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        // Cache hit — engine was NOT called again
        Assert.Equal(1, callCount);
    }

    [Fact(Timeout = 15_000)]
    public async Task EngineBidiFlowComposition_should_return_200ok_when_all_features_enabled()
    {
        var descriptor = new PipelineDescriptor(
            RedirectPolicy: new RedirectPolicy(),
            RetryPolicy: new RetryPolicy(),
            Expect100Policy: null, CompressionPolicy: null,
            CookieJar: new CookieJar(),
            CacheStore: new Cache(),
            CachePolicy: null,
            Handlers: [],
            AutomaticDecompression: true);

        var flow = BuildFlow(descriptor);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version11
        };

        var response = await RunSingleAsync(flow, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 15_000)]
    public async Task EngineBidiFlowComposition_should_retry_and_cache_with_cookies_when_all_features_enabled()
    {
        var jar = new CookieJar();
        var store = new Cache();
        var callCount = 0;

        byte[] Factory() => ++callCount == 1
            ? Response503()
            : "HTTP/1.1 200 OK\r\nCache-Control: max-age=3600\r\nDate: Thu, 21 Mar 2026 10:00:00 GMT\r\nContent-Length: 0\r\n\r\n"u8
                .ToArray();

        var descriptor = new PipelineDescriptor(
            RedirectPolicy: new RedirectPolicy(),
            RetryPolicy: new RetryPolicy(),
            Expect100Policy: null, CompressionPolicy: null,
            CookieJar: jar,
            CacheStore: store,
            CachePolicy: null,
            Handlers: [],
            AutomaticDecompression: true);

        var flow = BuildFlow(descriptor, Factory);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version11
        };

        var response = await RunSingleAsync(flow, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, callCount); // 503 + 200

        // Verify cache stored the response
        var cached = store.Get(new HttpRequestMessage(HttpMethod.Get, "http://example.com/"));
        Assert.NotNull(cached);
    }

    [Fact(Timeout = 10_000)]
    public async Task EngineBidiFlowComposition_should_decompress_when_automatic_decompression_enabled()
    {
        var plainBody = "Decompressed content!"u8.ToArray();
        var compressedBody = GzipCompress(plainBody);
        var header = $"HTTP/1.1 200 OK\r\nContent-Encoding: gzip\r\nContent-Length: {compressedBody.Length}\r\n\r\n";
        var headerBytes = System.Text.Encoding.Latin1.GetBytes(header);
        var responseBytes = new byte[headerBytes.Length + compressedBody.Length];
        headerBytes.CopyTo(responseBytes, 0);
        compressedBody.CopyTo(responseBytes, headerBytes.Length);

        var flow = BuildFlow(PipelineDescriptor.Empty, () => responseBytes);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/gzipped")
        {
            Version = HttpVersion.Version11
        };

        var response = await RunSingleAsync(flow, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(plainBody, body);
        Assert.False(response.Content.Headers.ContentEncoding.Contains("gzip"),
            "Content-Encoding: gzip should be removed after decompression");
    }

    [Fact(Timeout = 10_000)]
    public async Task
        EngineBidiFlowComposition_should_return_raw_compressed_bytes_when_automatic_decompression_disabled()
    {
        // AutomaticDecompression=false prevents the ContentEncodingBidiStage from being added.
        // Protocol-level decoders no longer decompress, so raw compressed
        // bytes are returned with Content-Encoding header preserved.
        var plainBody = "Protocol decoder handles this!"u8.ToArray();
        var compressedBody = GzipCompress(plainBody);
        var header = $"HTTP/1.1 200 OK\r\nContent-Encoding: gzip\r\nContent-Length: {compressedBody.Length}\r\n\r\n";
        var headerBytes = System.Text.Encoding.Latin1.GetBytes(header);
        var responseBytes = new byte[headerBytes.Length + compressedBody.Length];
        headerBytes.CopyTo(responseBytes, 0);
        compressedBody.CopyTo(responseBytes, headerBytes.Length);

        var descriptor = new PipelineDescriptor(
            RedirectPolicy: null, RetryPolicy: null, Expect100Policy: null, CompressionPolicy: null, CookieJar: null,
            CacheStore: null, CachePolicy: null, Handlers: [],
            AutomaticDecompression: false);

        var flow = BuildFlow(descriptor, () => responseBytes);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/gzipped")
        {
            Version = HttpVersion.Version11
        };

        var response = await RunSingleAsync(flow, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Raw compressed bytes returned — Content-Encoding header preserved
        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(compressedBody, body);
        Assert.True(response.Content.Headers.Contains("Content-Encoding"));
        Assert.Equal("gzip", string.Join("", response.Content.Headers.GetValues("Content-Encoding")));
    }

    private static byte[] GzipCompress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionMode.Compress))
        {
            gzip.Write(data, 0, data.Length);
        }

        return output.ToArray();
    }
}