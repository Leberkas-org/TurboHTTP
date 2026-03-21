using System.IO.Compression;
using System.Net;
using Akka;
using Akka.Streams.Dsl;
using TurboHttp.Internal;
using TurboHttp.Protocol.RFC6265;
using TurboHttp.Protocol.RFC9110;
using TurboHttp.Protocol.RFC9111;
using TurboHttp.Streams;

namespace TurboHttp.StreamTests.Streams;

/// <summary>
/// Tests the BidiFlow Atop composition in <see cref="Engine.BuildExtendedPipeline"/>.
/// Verifies conditional inclusion, minimal graph for <see cref="PipelineDescriptor.Empty"/>,
/// each feature in isolation, and all features combined.
/// </summary>
public sealed class EngineBidiFlowCompositionTests : EngineTestBase
{
    private static byte[] Ok200() =>
        "HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

    private static byte[] Response503() =>
        "HTTP/1.1 503 Service Unavailable\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

    private static byte[] Response301() =>
        "HTTP/1.1 301 Moved Permanently\r\nLocation: http://example.com/new\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

    private static byte[] Response200WithSetCookie() =>
        "HTTP/1.1 200 OK\r\nSet-Cookie: token=xyz; Domain=example.com; Path=/\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

    private static Flow<IOutputItem, IInputItem, NotUsed> NoOpH2Flow()
        => Flow.FromGraph(new H2EngineFakeConnectionStage());

    private async Task<HttpResponseMessage> RunSingleAsync(
        Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> flow,
        HttpRequestMessage request)
    {
        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        _ = Source.Single(request)
            .Concat(Source.Never<HttpRequestMessage>())
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(r => tcs.TrySetResult(r)), Materializer);
        return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> BuildFlow(
        PipelineDescriptor descriptor,
        Func<byte[]>? http11ResponseFactory = null)
    {
        http11ResponseFactory ??= Ok200;
        var engine = new Engine();
        return engine.CreateFlow(
            () => Flow.FromGraph(new EngineFakeConnectionStage(Ok200)),
            () => Flow.FromGraph(new EngineFakeConnectionStage(http11ResponseFactory)),
            NoOpH2Flow,
            NoOpH2Flow,
            descriptor);
    }

    // ---- PipelineDescriptor.Empty (minimal graph) ----

    [Fact(Timeout = 10_000,
        DisplayName = "EBFC-001: Empty descriptor — 200 OK flows through minimal graph")]
    public async Task Should_Return200Ok_When_EmptyDescriptor()
    {
        var flow = BuildFlow(PipelineDescriptor.Empty);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version11
        };

        var response = await RunSingleAsync(flow, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 10_000,
        DisplayName = "EBFC-002: Empty descriptor — 503 passes through (no retry)")]
    public async Task Should_PassThrough503_When_EmptyDescriptor()
    {
        var flow = BuildFlow(PipelineDescriptor.Empty, Response503);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version11
        };

        var response = await RunSingleAsync(flow, request);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact(Timeout = 10_000,
        DisplayName = "EBFC-003: Empty descriptor — 301 passes through (no redirect)")]
    public async Task Should_PassThrough301_When_EmptyDescriptor()
    {
        var flow = BuildFlow(PipelineDescriptor.Empty, Response301);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version11
        };

        var response = await RunSingleAsync(flow, request);
        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
    }

    // ---- AutomaticDecompression = false ----

    [Fact(Timeout = 10_000,
        DisplayName = "EBFC-004: AutomaticDecompression=false — no DecompressionBidiStage in chain")]
    public async Task Should_StillDeliverResponse_When_AutomaticDecompressionDisabled()
    {
        // Note: decompression also happens in the protocol-level decoders (Http11Decoder,
        // Http20StreamStage), so AutomaticDecompression=false only removes the BidiStage
        // from the feature chain — it does not prevent protocol-level decompression.
        // This test verifies the pipeline still works correctly with the flag disabled.
        var descriptor = new PipelineDescriptor(
            RedirectPolicy: null, RetryPolicy: null, CookieJar: null,
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

    // ---- Individual features in isolation ----

    [Fact(Timeout = 10_000,
        DisplayName = "EBFC-005: Only RetryPolicy — 503 retried, 200 returned")]
    public async Task Should_RetryOn503_When_OnlyRetryPolicyIsSet()
    {
        var callCount = 0;
        byte[] Factory() => ++callCount == 1 ? Response503() : Ok200();

        var descriptor = new PipelineDescriptor(
            RedirectPolicy: null, RetryPolicy: new RetryPolicy(),
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

    [Fact(Timeout = 15_000,
        DisplayName = "EBFC-006: Only RedirectPolicy — 301 followed, 200 returned")]
    public async Task Should_FollowRedirect_When_OnlyRedirectPolicyIsSet()
    {
        var callCount = 0;
        byte[] Factory() => ++callCount == 1 ? Response301() : Ok200();

        var descriptor = new PipelineDescriptor(
            RedirectPolicy: new RedirectPolicy(), RetryPolicy: null,
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

    [Fact(Timeout = 10_000,
        DisplayName = "EBFC-007: Only CookieJar — Set-Cookie stored on response")]
    public async Task Should_StoreCookieFromResponse_When_OnlyCookieJarIsSet()
    {
        var jar = new CookieJar();
        var descriptor = new PipelineDescriptor(
            RedirectPolicy: null, RetryPolicy: null,
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

    [Fact(Timeout = 10_000,
        DisplayName = "EBFC-008: Only CacheStore — second request served from cache")]
    public async Task Should_ServeCachedResponse_When_OnlyCacheStoreIsSet()
    {
        var store = new HttpCacheStore();
        var callCount = 0;

        byte[] Factory()
        {
            callCount++;
            return "HTTP/1.1 200 OK\r\nCache-Control: max-age=3600\r\nDate: Thu, 21 Mar 2026 10:00:00 GMT\r\nContent-Length: 5\r\n\r\nhello"u8.ToArray();
        }

        var descriptor = new PipelineDescriptor(
            RedirectPolicy: null, RetryPolicy: null,
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

    // ---- All features combined ----

    [Fact(Timeout = 15_000,
        DisplayName = "EBFC-009: All features — 200 OK flows through complete BidiFlow chain")]
    public async Task Should_Return200Ok_When_AllFeaturesEnabled()
    {
        var descriptor = new PipelineDescriptor(
            RedirectPolicy: new RedirectPolicy(),
            RetryPolicy: new RetryPolicy(),
            CookieJar: new CookieJar(),
            CacheStore: new HttpCacheStore(),
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

    [Fact(Timeout = 15_000,
        DisplayName = "EBFC-010: All features — 503→retry→200 with cookies and cache")]
    public async Task Should_RetryAndCacheWithCookies_When_AllFeaturesEnabled()
    {
        var jar = new CookieJar();
        var store = new HttpCacheStore();
        var callCount = 0;

        byte[] Factory() => ++callCount == 1 ? Response503() :
            "HTTP/1.1 200 OK\r\nCache-Control: max-age=3600\r\nDate: Thu, 21 Mar 2026 10:00:00 GMT\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var descriptor = new PipelineDescriptor(
            RedirectPolicy: new RedirectPolicy(),
            RetryPolicy: new RetryPolicy(),
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

    // ---- AutomaticDecompression = true (default) ----

    [Fact(Timeout = 10_000,
        DisplayName = "EBFC-011: AutomaticDecompression=true — gzip response decompressed")]
    public async Task Should_Decompress_When_AutomaticDecompressionEnabled()
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

        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(plainBody, body);
        Assert.False(response.Content.Headers.ContentEncoding.Contains("gzip"),
            "Content-Encoding: gzip should be removed after decompression");
    }

    [Fact(Timeout = 10_000,
        DisplayName = "EBFC-012: AutomaticDecompression=false — gzip still decompressed by protocol decoder")]
    public async Task Should_StillDecompress_When_AutomaticDecompressionDisabledButProtocolDecoderHandlesIt()
    {
        // AutomaticDecompression=false removes the DecompressionBidiStage from the feature chain,
        // but the protocol-level decoders (Http11Decoder, Http20StreamStage) handle Content-Encoding
        // decompression per RFC 9110 §8.4. So gzip responses are still decompressed.
        var plainBody = "Protocol decoder handles this!"u8.ToArray();
        var compressedBody = GzipCompress(plainBody);
        var header = $"HTTP/1.1 200 OK\r\nContent-Encoding: gzip\r\nContent-Length: {compressedBody.Length}\r\n\r\n";
        var headerBytes = System.Text.Encoding.Latin1.GetBytes(header);
        var responseBytes = new byte[headerBytes.Length + compressedBody.Length];
        headerBytes.CopyTo(responseBytes, 0);
        compressedBody.CopyTo(responseBytes, headerBytes.Length);

        var descriptor = new PipelineDescriptor(
            RedirectPolicy: null, RetryPolicy: null, CookieJar: null,
            CacheStore: null, CachePolicy: null, Handlers: [],
            AutomaticDecompression: false);

        var flow = BuildFlow(descriptor, () => responseBytes);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/gzipped")
        {
            Version = HttpVersion.Version11
        };

        var response = await RunSingleAsync(flow, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Protocol-level decoder (Http11Decoder) decompresses gzip regardless of AutomaticDecompression flag
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(plainBody, body);
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
