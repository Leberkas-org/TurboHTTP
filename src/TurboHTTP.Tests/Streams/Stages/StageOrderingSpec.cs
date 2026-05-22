using System.IO.Compression;
using System.Net;
using Akka;
using Akka.Streams.Dsl;
using Servus.Akka.Transport;
using TurboHTTP.Client;
using TurboHTTP.Features.Caching;
using TurboHTTP.Features.Cookies;
using TurboHTTP.Protocol.Semantics;
using TurboHTTP.Streams;
using TurboHTTP.Streams.Stages.Client;
using TurboHTTP.Streams.Stages.Features;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Streams.Stages;

public sealed class StageOrderingSpec : EngineTestBase
{
    private static CookieJar JarWithCookie(string name, string value, string domain, string path = "/")
    {
        var jar = new CookieJar();
        var response = new HttpResponseMessage();
        response.Headers.TryAddWithoutValidation("Set-Cookie", $"{name}={value}; Domain={domain}; Path={path}");
        jar.ProcessResponse(new Uri($"http://{domain}/"), response);
        return jar;
    }

    private static HttpResponseMessage MakeCacheableResponse(
        string url,
        string? cacheControl = "max-age=3600",
        byte[]? body = null,
        string? setCookie = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, url),
            Content = new ByteArrayContent(body ?? [])
        };
        if (cacheControl is not null)
        {
            response.Headers.TryAddWithoutValidation("Cache-Control", cacheControl);
        }

        response.Headers.Date = DateTimeOffset.UtcNow;
        if (setCookie is not null)
        {
            response.Headers.TryAddWithoutValidation("Set-Cookie", setCookie);
        }

        return response;
    }

    private static Cache StoreWithFreshEntry(
        string url = "http://example.com/resource",
        string? varyHeader = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("Cookie", "session=abc123");
        var resp = new HttpResponseMessage(HttpStatusCode.OK);
        resp.Headers.TryAddWithoutValidation("Cache-Control", "max-age=3600");
        resp.Headers.Date = DateTimeOffset.UtcNow;
        if (varyHeader is not null)
        {
            resp.Headers.TryAddWithoutValidation("Vary", varyHeader);
        }

        var store = new Cache();
        var (owner, length) = Cache.RentBody([]);
        store.Put(req, resp, owner, length,
            DateTimeOffset.UtcNow.AddSeconds(-1), DateTimeOffset.UtcNow);
        return store;
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

    private static Flow<ITransportOutbound, ITransportInbound, NotUsed> Http11Flow(Func<byte[]> responseFactory)
        => CreateFakeConnectionFlow(responseFactory);

    private static Flow<ITransportOutbound, ITransportInbound, NotUsed> Http10Flow(Func<byte[]> responseFactory)
        => CreateFakeConnectionFlow(responseFactory);

    private static Flow<ITransportOutbound, ITransportInbound, NotUsed> NoOpH2Flow()
        => CreateFakeConnectionFlow(() => []);

    private static byte[] Ok11Response()
        => "HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

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
    public async Task
        StageOrdering_should_have_cookie_header_when_reaching_engine_when_cookie_bidi_is_outer_to_cache_bidi()
    {
        // CookieBidi.Atop(CacheBidi): request path is Cookie → Cache → Engine.
        // The echo engine returns the request as RequestMessage, proving cookies were injected first.
        var jar = JarWithCookie("session", "abc123", "example.com");
        var store = new Cache();

        var bidi = BidiFlow.FromGraph(new CookieBidiStage(jar))
            .Atop(BidiFlow.FromGraph(new CacheBidiStage(store)));

        var echo = Flow.Create<HttpRequestMessage>()
            .Select(req => new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = req });

        var flow = bidi.Join(echo);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page");
        var response = await RunSingleAsync(flow, request);

        Assert.True(response.RequestMessage!.Headers.Contains("Cookie"),
            "CookieBidi must run before CacheBidi: Cookie header should be present on the request reaching the engine");
        var cookieValue = string.Join("; ", response.RequestMessage.Headers.GetValues("Cookie"));
        Assert.Contains("session=abc123", cookieValue);
    }

    [Fact(Timeout = 15_000)]
    public async Task StageOrdering_should_inject_fresh_cookies_when_redirect_bidi_is_outer_to_cookie_bidi()
    {
        // RedirectBidi.Atop(CookieBidi): redirect request flows through CookieBidi on the way to the engine.
        // RedirectBidi is outermost → redirect request enters CookieBidi → cookies injected for new domain.
        var jar = JarWithCookie("auth", "token456", "new-domain.com");
        var callCount = 0;

        var bidi = BidiFlow.FromGraph(new RedirectBidiStage(new RedirectPolicy()))
            .Atop(BidiFlow.FromGraph(new CookieBidiStage(jar)));

        var engine = Flow.Create<HttpRequestMessage>()
            .Select(req =>
            {
                if (Interlocked.Increment(ref callCount) == 1)
                {
                    var resp = new HttpResponseMessage(HttpStatusCode.MovedPermanently) { RequestMessage = req };
                    resp.Headers.TryAddWithoutValidation("Location", "http://new-domain.com/target");
                    return resp;
                }

                return new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = req };
            });

        var flow = bidi.Join(engine);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/old");
        var response = await RunSingleAsync(flow, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, callCount);
        Assert.True(response.RequestMessage!.Headers.Contains("Cookie"),
            "Redirect enters before CookieBidi: fresh cookies must be injected for new domain");
        var cookieValue = string.Join("; ", response.RequestMessage.Headers.GetValues("Cookie"));
        Assert.Contains("auth=token456", cookieValue);
    }

    [Fact(Timeout = 10_000)]
    public async Task StageOrdering_should_preserve_original_cookies_when_retry_bidi_is_inner_to_cookie_bidi()
    {
        // CookieBidi.Atop(RetryBidi): retry feedback stays inside RetryBidi, bypassing CookieBidi.
        // Cookies injected on first pass are preserved because RetryBidi reuses the original request.
        var jar = JarWithCookie("session", "abc123", "example.com");
        var callCount = 0;
        HttpRequestMessage? retriedRequest = null;

        var bidi = BidiFlow.FromGraph(new CookieBidiStage(jar))
            .Atop(BidiFlow.FromGraph(new RetryBidiStage(new RetryPolicy())));

        var engine = Flow.Create<HttpRequestMessage>()
            .Select(req =>
            {
                var count = Interlocked.Increment(ref callCount);
                if (count == 2)
                {
                    retriedRequest = req;
                }

                if (count == 1)
                {
                    return new HttpResponseMessage(HttpStatusCode.RequestTimeout) { RequestMessage = req };
                }

                return new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = req };
            });

        var flow = bidi.Join(engine);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        var response = await RunSingleAsync(flow, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, callCount);
        Assert.True(retriedRequest!.Headers.Contains("Cookie"),
            "Retry feedback is inner to CookieBidi: cookies are preserved from the first pass");
        var cookieValue = string.Join("; ", retriedRequest.Headers.GetValues("Cookie"));
        Assert.Contains("session=abc123", cookieValue);
    }

    [Fact(Timeout = 10_000)]
    public void StageOrdering_should_override_default_version_when_request_enricher_processes_request()
    {
        // Prove that RequestEnricher overrides Version when it's the 1.1 default
        // and DefaultRequestVersion differs. Since redirect enters AFTER enricher,
        // a redirected request with Version 2.0 (set by BuildRedirectRequest) is NOT overridden.
        var options = new TurboRequestOptions(
            BaseAddress: null,
            DefaultRequestHeaders: new HttpRequestMessage().Headers,
            DefaultRequestVersion: new Version(2, 0),
            DefaultVersionPolicy: HttpVersionPolicy.RequestVersionOrHigher,
            Timeout: TimeSpan.FromSeconds(30));

        var enricher = new RequestEnricher(() => options);

        // Request with default Version 1.1 — enricher overrides to 2.0
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        Assert.Equal(HttpVersion.Version11, request.Version); // default

        var result = enricher.Enrich(request);
        Assert.Equal(new Version(2, 0), result.Version);
        // This proves: enricher DOES modify Version for new requests at the 1.1 default.
        // A redirected request with Version 2.0 (non-default) would NOT be modified,
        // because it enters AFTER enricher (INV-9) and even if it entered before,
        // the enricher only overrides when Version == 1.1 (the default).
    }

    [Fact(Timeout = 10_000)]
    public async Task StageOrdering_should_store_cookies_and_cache_response_when_cookie_bidi_and_cache_bidi_composed()
    {
        // CookieBidi.Atop(CacheBidi): response path is Engine → CacheBidi(store) → CookieBidi(store Set-Cookie).
        // Both storage operations happen; order is reversed from old architecture but functionally equivalent.
        var jar = new CookieJar();
        var store = new Cache();

        var bidi = BidiFlow.FromGraph(new CookieBidiStage(jar))
            .Atop(BidiFlow.FromGraph(new CacheBidiStage(store)));

        var engine = Flow.Create<HttpRequestMessage>()
            .Select(req =>
            {
                var resp = MakeCacheableResponse(
                    "http://example.com/page",
                    cacheControl: "max-age=3600",
                    body: "hello"u8.ToArray(),
                    setCookie: "token=xyz; Domain=example.com; Path=/");
                resp.RequestMessage = req;
                return resp;
            });

        var flow = bidi.Join(engine);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page");
        await RunSingleAsync(flow, request);

        // Verify cookie was stored (CookieBidi response direction)
        var nextRequest = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page");
        jar.AddCookiesToRequest(new Uri("http://example.com/page"), ref nextRequest);
        Assert.True(nextRequest.Headers.Contains("Cookie"), "CookieBidi must store Set-Cookie from response");

        // Verify response was cached (CacheBidi response direction)
        var cacheResult = store.Get(new HttpRequestMessage(HttpMethod.Get, "http://example.com/page"));
        Assert.NotNull(cacheResult);
    }

    [Fact(Timeout = 10_000)]
    public async Task
        StageOrdering_should_cache_response_before_retry_evaluation_when_cache_bidi_is_inner_to_retry_bidi()
    {
        // RetryBidi.Atop(CacheBidi): response path is Engine → CacheBidi(store) → RetryBidi(evaluate: 200=pass).
        var store = new Cache();

        var bidi = BidiFlow.FromGraph(new RetryBidiStage(new RetryPolicy()))
            .Atop(BidiFlow.FromGraph(new CacheBidiStage(store)));

        var engine = Flow.Create<HttpRequestMessage>()
            .Select(req =>
            {
                var resp = MakeCacheableResponse("http://example.com/data", body: "data"u8.ToArray());
                resp.RequestMessage = req;
                return resp;
            });

        var flow = bidi.Join(engine);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/data");
        var response = await RunSingleAsync(flow, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Verify response was cached (CacheBidi processes first on response path)
        var cacheResult = store.Get(new HttpRequestMessage(HttpMethod.Get, "http://example.com/data"));
        Assert.NotNull(cacheResult);
    }

    [Fact(Timeout = 10_000)]
    public async Task StageOrdering_should_pass_through_200ok_when_retry_bidi_and_redirect_bidi_composed()
    {
        // RedirectBidi.Atop(RetryBidi): response path is Engine → RetryBidi(200=pass) → RedirectBidi(200=pass).
        var bidi = BidiFlow.FromGraph(new RedirectBidiStage(new RedirectPolicy()))
            .Atop(BidiFlow.FromGraph(new RetryBidiStage(new RetryPolicy())));

        var echo = Flow.Create<HttpRequestMessage>()
            .Select(req => new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = req });

        var flow = bidi.Join(echo);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        var response = await RunSingleAsync(flow, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 10_000)]
    public async Task StageOrdering_should_bypass_engine_when_cache_hit_occurs()
    {
        // RetryBidi.Atop(CacheBidi): cache hit produces response on CacheBidi.Out2 → RetryBidi.In2 → Out2.
        // Engine is never called because CacheBidi does not push request on Out1.
        var store = StoreWithFreshEntry("http://example.com/cached");
        var engineCallCount = 0;

        var bidi = BidiFlow.FromGraph(new RetryBidiStage(new RetryPolicy()))
            .Atop(BidiFlow.FromGraph(new CacheBidiStage(store)));

        var engine = Flow.Create<HttpRequestMessage>()
            .Select(req =>
            {
                Interlocked.Increment(ref engineCallCount);
                return new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = req };
            });

        var flow = bidi.Join(engine);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/cached");
        var response = await RunSingleAsync(flow, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, engineCallCount);
    }

    [Fact(Timeout = 10_000)]
    public async Task
        StageOrdering_should_store_response_in_jar_and_cache_when_engine_processes_before_bidi_flow_chain()
    {
        // Engine-level test: ConnectionReuseStage is inside the engine; CookieBidi/CacheBidi are outside.
        // The response flows: Engine(ConnectionReuse) → BidiFlow(CacheBidi → CookieBidi) → Client.
        var jar = new CookieJar();
        var store = new Cache();

        byte[] ResponseWithCookieAndCache() =>
            "HTTP/1.1 200 OK\r\nSet-Cookie: k=v; Domain=example.com; Path=/\r\nCache-Control: max-age=3600\r\nDate: Thu, 21 Mar 2026 10:00:00 GMT\r\nContent-Length: 4\r\n\r\nbody"u8
                .ToArray();

        var descriptor = new PipelineDescriptor(
            RedirectPolicy: null,
            RetryPolicy: null,
            Expect100Policy: null,
            CompressionPolicy: null,
            CookieJar: jar,
            CacheStore: store,
            CachePolicy: null,
            Handlers: []);

        var engine = new Engine();
        var transports = new TransportRegistry()
            .Register(new Version(1, 0), Http10Flow(ResponseWithCookieAndCache))
            .Register(new Version(1, 1), Http11Flow(ResponseWithCookieAndCache))
            .Register(new Version(2, 0), NoOpH2Flow())
            .Register(new Version(3, 0), NoOpH2Flow());
        var flow = engine.CreateFlow(transports, descriptor);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource")
        {
            Version = HttpVersion.Version11
        };

        var response = await RunSingleAsync(flow, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Verify both post-processing stages ran (after engine including ConnectionReuse)
        var nextReq = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        jar.AddCookiesToRequest(new Uri("http://example.com/resource"), ref nextReq);
        Assert.True(nextReq.Headers.Contains("Cookie"));

        var cacheResult = store.Get(new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource"));
        Assert.NotNull(cacheResult);
    }

    [Fact(Timeout = 10_000)]
    public async Task StageOrdering_should_cache_decompressed_body_when_decompression_bidi_is_inner_to_cache_bidi()
    {
        // CacheBidi.Atop(DecompressionBidi): response path is Engine → Decomp(decompress) → Cache(store).
        // Cached body is the decompressed version.
        var store = new Cache();
        var plainBody = "Hello, decompressed world!"u8.ToArray();

        var bidi = BidiFlow.FromGraph(new CacheBidiStage(store))
            .Atop(BidiFlow.FromGraph(new ContentEncodingBidiStage()));

        var engine = Flow.Create<HttpRequestMessage>()
            .Select(req =>
            {
                var compressed = GzipCompress(plainBody);
                var resp = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    RequestMessage = req,
                    Content = new ByteArrayContent(compressed)
                };
                resp.Content.Headers.TryAddWithoutValidation("Content-Encoding", "gzip");
                resp.Content.Headers.ContentLength = compressed.Length;
                resp.Headers.TryAddWithoutValidation("Cache-Control", "max-age=3600");
                resp.Headers.Date = DateTimeOffset.UtcNow;
                return resp;
            });

        var flow = bidi.Join(engine);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/compressed");
        var response = await RunSingleAsync(flow, request);

        // Verify the output response body is decompressed
        var outputBody = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(plainBody, outputBody);

        // Verify the cached entry stores the decompressed body
        var lookupReq = new HttpRequestMessage(HttpMethod.Get, "http://example.com/compressed");
        var cacheResult = store.Get(lookupReq);
        Assert.NotNull(cacheResult);
        Assert.True(cacheResult.Body.Span.SequenceEqual(plainBody));
    }
}

