using System.IO.Compression;
using System.Net;
using System.Text;
using TurboHttp.Protocol;

namespace TurboHttp.Tests.Integration;

/// <summary>
/// Phase 59 — Cross-Feature Integrity Validation.
/// Verifies that independently-implemented protocol features compose correctly
/// without semantic interference.
///
/// Groups:
///   CFI-001..010  Redirect + Cookies (domain re-evaluation)
///   CFI-011..020  Redirect + Authorization (cross-origin stripping)
///   CFI-021..030  Decompression + Entity integrity
///   CFI-031..040  Pooling + Connection lifecycle (no leaked connections)
///   CFI-041..050  Retry + Streaming (only retry rewindable bodies)
///   CFI-051..060  HEAD — never expose body even with Content-Encoding
/// </summary>
public sealed class CrossFeatureIntegrityTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static HttpResponseMessage BuildRedirect(HttpStatusCode status, string location)
    {
        var r = new HttpResponseMessage(status);
        r.Headers.TryAddWithoutValidation("Location", location);
        return r;
    }

    private static byte[] GzipCompress(string text)
    {
        var raw = Encoding.UTF8.GetBytes(text);
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Fastest))
        {
            gz.Write(raw);
        }

        return ms.ToArray();
    }

    private static byte[] DeflateCompress(string text)
    {
        var raw = Encoding.UTF8.GetBytes(text);
        using var ms = new MemoryStream();
        // ZLibStream produces the RFC 1950 (zlib) format that "deflate" decodes by default.
        using (var zlib = new ZLibStream(ms, CompressionLevel.Fastest))
        {
            zlib.Write(raw);
        }

        return ms.ToArray();
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // GROUP 1 — Redirect + Cookies
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "CFI-001: Cross-domain redirect — cookie not forwarded to new domain")]
    public void Redirect_CrossDomain_CookieNotForwardedToNewDomain()
    {
        var jar = new CookieJar();

        // Seed a cookie for example.com
        var originalUri = new Uri("http://example.com/path");
        var seedResponse = new HttpResponseMessage(HttpStatusCode.OK);
        seedResponse.Headers.TryAddWithoutValidation("Set-Cookie", "session=abc; Domain=example.com; Path=/");
        jar.ProcessResponse(originalUri, seedResponse);

        // Redirect to a completely different domain
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/path");
        var redirectResponse = BuildRedirect(HttpStatusCode.Found, "http://other.com/page");

        var handler = new RedirectHandler();
        var newRequest = handler.BuildRedirectRequest(original, redirectResponse, jar);

        // The new request must NOT have a Cookie header for other.com
        Assert.False(newRequest.Headers.Contains("Cookie"),
            "Cookie must not be blindly forwarded to a different domain on redirect.");
    }

    [Fact(DisplayName = "CFI-002: Same-domain redirect — applicable cookies forwarded")]
    public void Redirect_SameDomain_CookieForwarded()
    {
        var jar = new CookieJar();

        var originalUri = new Uri("http://example.com/a");
        var seedResponse = new HttpResponseMessage(HttpStatusCode.OK);
        seedResponse.Headers.TryAddWithoutValidation("Set-Cookie", "token=xyz; Domain=example.com; Path=/");
        jar.ProcessResponse(originalUri, seedResponse);

        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/a");
        var redirectResponse = BuildRedirect(HttpStatusCode.MovedPermanently, "http://example.com/b");

        var handler = new RedirectHandler();
        var newRequest = handler.BuildRedirectRequest(original, redirectResponse, jar);

        Assert.True(newRequest.Headers.Contains("Cookie"),
            "Cookie applicable to the same domain must be forwarded on redirect.");
    }

    [Fact(DisplayName = "CFI-003: Set-Cookie in redirect response is processed before new request")]
    public void Redirect_SetCookieInRedirectResponse_ProcessedBeforeNewRequest()
    {
        var jar = new CookieJar();

        // The redirect response sets a new cookie for example.com
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/old");
        var redirectResponse = BuildRedirect(HttpStatusCode.Found, "http://example.com/new");
        redirectResponse.Headers.TryAddWithoutValidation("Set-Cookie", "csrf=tok; Domain=example.com; Path=/");

        var handler = new RedirectHandler();
        var newRequest = handler.BuildRedirectRequest(original, redirectResponse, jar);

        // The cookie set by the redirect response must now be in the new request
        Assert.True(newRequest.Headers.Contains("Cookie"),
            "Cookie set in redirect response must be applied to the new redirect request.");
    }

    [Fact(DisplayName = "CFI-004: Secure cookie not forwarded on HTTP redirect")]
    public void Redirect_SecureCookie_NotForwardedOnHttp()
    {
        var jar = new CookieJar();

        // Seed a Secure cookie via HTTPS response
        var httpsUri = new Uri("https://example.com/login");
        var seedResponse = new HttpResponseMessage(HttpStatusCode.OK);
        seedResponse.Headers.TryAddWithoutValidation("Set-Cookie", "auth=secret; Secure; Domain=example.com; Path=/");
        jar.ProcessResponse(httpsUri, seedResponse);

        // Redirect is to HTTP — Secure cookie must not be forwarded
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page");
        var redirectResponse = BuildRedirect(HttpStatusCode.Found, "http://example.com/other");

        var handler = new RedirectHandler();
        var newRequest = handler.BuildRedirectRequest(original, redirectResponse, jar);

        Assert.False(newRequest.Headers.Contains("Cookie"),
            "Secure cookie must not be forwarded when the redirect target is HTTP.");
    }

    [Fact(DisplayName = "CFI-005: Cookie forwarded on same-domain HTTPS redirect")]
    public void Redirect_SecureCookie_ForwardedOnHttps()
    {
        var jar = new CookieJar();

        var httpsUri = new Uri("https://example.com/login");
        var seedResponse = new HttpResponseMessage(HttpStatusCode.OK);
        seedResponse.Headers.TryAddWithoutValidation("Set-Cookie", "auth=secret; Secure; Domain=example.com; Path=/");
        jar.ProcessResponse(httpsUri, seedResponse);

        var original = new HttpRequestMessage(HttpMethod.Get, "https://example.com/page");
        var redirectResponse = BuildRedirect(HttpStatusCode.Found, "https://example.com/other");

        var handler = new RedirectHandler();
        var newRequest = handler.BuildRedirectRequest(original, redirectResponse, jar);

        Assert.True(newRequest.Headers.Contains("Cookie"),
            "Secure cookie must be forwarded on an HTTPS redirect to the same domain.");
    }

    [Fact(DisplayName = "CFI-006: Path-scoped cookie not forwarded when redirect leaves scope")]
    public void Redirect_PathScopedCookie_NotForwardedWhenOutOfPath()
    {
        var jar = new CookieJar();

        var uri = new Uri("http://example.com/admin/");
        var seedResponse = new HttpResponseMessage(HttpStatusCode.OK);
        seedResponse.Headers.TryAddWithoutValidation("Set-Cookie", "admin_token=x; Domain=example.com; Path=/admin");
        jar.ProcessResponse(uri, seedResponse);

        // Redirect goes to /public — outside /admin path
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/admin/page");
        var redirectResponse = BuildRedirect(HttpStatusCode.Found, "http://example.com/public/page");

        var handler = new RedirectHandler();
        var newRequest = handler.BuildRedirectRequest(original, redirectResponse, jar);

        Assert.False(newRequest.Headers.Contains("Cookie"),
            "Path-scoped cookie must not be forwarded when redirect target is outside the cookie's path.");
    }

    [Fact(DisplayName = "CFI-007: Cookie jar handles multiple cookies; only matching ones forwarded")]
    public void Redirect_MultipleCookies_OnlyMatchingForwarded()
    {
        var jar = new CookieJar();

        // Cookie for /api path
        var apiUri = new Uri("http://example.com/api/");
        var apiResponse = new HttpResponseMessage(HttpStatusCode.OK);
        apiResponse.Headers.TryAddWithoutValidation("Set-Cookie", "api_key=1; Domain=example.com; Path=/api");
        jar.ProcessResponse(apiUri, apiResponse);

        // Cookie for all paths
        var rootUri = new Uri("http://example.com/");
        var rootResponse = new HttpResponseMessage(HttpStatusCode.OK);
        rootResponse.Headers.TryAddWithoutValidation("Set-Cookie", "session=2; Domain=example.com; Path=/");
        jar.ProcessResponse(rootUri, rootResponse);

        // Redirect to /public (not /api) — only session cookie should be forwarded
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/api/item");
        var redirectResponse = BuildRedirect(HttpStatusCode.Found, "http://example.com/public/item");

        var handler = new RedirectHandler();
        var newRequest = handler.BuildRedirectRequest(original, redirectResponse, jar);

        // session cookie (Path=/) should be forwarded; api_key cookie (Path=/api) should not
        Assert.True(newRequest.Headers.Contains("Cookie"),
            "Root-path cookie must be forwarded to any path on redirect.");

        var cookieHeader = string.Join("; ", newRequest.Headers.GetValues("Cookie"));
        Assert.Contains("session=2", cookieHeader);
        Assert.DoesNotContain("api_key", cookieHeader);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // GROUP 2 — Redirect + Authorization
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "CFI-011: Cross-origin redirect strips Authorization header")]
    public void Redirect_CrossOrigin_StripsAuthorizationHeader()
    {
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/secure");
        original.Headers.TryAddWithoutValidation("Authorization", "Bearer token123");

        var redirectResponse = BuildRedirect(HttpStatusCode.Found, "http://other.com/secure");

        var handler = new RedirectHandler();
        var newRequest = handler.BuildRedirectRequest(original, redirectResponse);

        Assert.False(newRequest.Headers.Contains("Authorization"),
            "Authorization header must be stripped on cross-origin redirect.");
    }

    [Fact(DisplayName = "CFI-012: Same-origin redirect preserves Authorization header")]
    public void Redirect_SameOrigin_PreservesAuthorizationHeader()
    {
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page1");
        original.Headers.TryAddWithoutValidation("Authorization", "Bearer token123");

        var redirectResponse = BuildRedirect(HttpStatusCode.MovedPermanently, "http://example.com/page2");

        var handler = new RedirectHandler();
        var newRequest = handler.BuildRedirectRequest(original, redirectResponse);

        Assert.True(newRequest.Headers.Contains("Authorization"),
            "Authorization header must be preserved on same-origin redirect.");
    }

    [Fact(DisplayName = "CFI-013: Cross-scheme (HTTP to HTTPS same host) strips Authorization")]
    public void Redirect_CrossScheme_SameHost_StripsAuthorization()
    {
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page");
        original.Headers.TryAddWithoutValidation("Authorization", "Basic dXNlcjpwYXNz");

        // Different scheme = different origin
        var redirectResponse = BuildRedirect(HttpStatusCode.MovedPermanently, "https://example.com/page");

        var handler = new RedirectHandler();
        var newRequest = handler.BuildRedirectRequest(original, redirectResponse);

        Assert.False(newRequest.Headers.Contains("Authorization"),
            "Authorization must be stripped when scheme changes (cross-origin by scheme).");
    }

    [Fact(DisplayName = "CFI-014: 307 same-origin redirect preserves Authorization and body")]
    public void Redirect_307_SameOrigin_PreservesAuthorizationAndBody()
    {
        var original = new HttpRequestMessage(HttpMethod.Post, "http://example.com/submit")
        {
            Content = new StringContent("payload")
        };
        original.Headers.TryAddWithoutValidation("Authorization", "Bearer token");

        var redirectResponse = BuildRedirect(HttpStatusCode.TemporaryRedirect, "http://example.com/submit2");

        var handler = new RedirectHandler();
        var newRequest = handler.BuildRedirectRequest(original, redirectResponse);

        Assert.Equal(HttpMethod.Post, newRequest.Method);
        Assert.NotNull(newRequest.Content);
        Assert.True(newRequest.Headers.Contains("Authorization"),
            "307 same-origin redirect must preserve Authorization and body.");
    }

    [Fact(DisplayName = "CFI-015: Cross-origin 307 strips Authorization but preserves method and body")]
    public void Redirect_307_CrossOrigin_StripsAuthorization_ButPreservesMethodAndBody()
    {
        var original = new HttpRequestMessage(HttpMethod.Post, "http://example.com/submit")
        {
            Content = new StringContent("payload")
        };
        original.Headers.TryAddWithoutValidation("Authorization", "Bearer token");

        var redirectResponse = BuildRedirect(HttpStatusCode.TemporaryRedirect, "http://other.com/submit2");

        var handler = new RedirectHandler();
        var newRequest = handler.BuildRedirectRequest(original, redirectResponse);

        // 307 preserves method and body even cross-origin, but strips Authorization
        Assert.Equal(HttpMethod.Post, newRequest.Method);
        Assert.NotNull(newRequest.Content);
        Assert.False(newRequest.Headers.Contains("Authorization"),
            "Authorization must be stripped on cross-origin redirect even for 307.");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // GROUP 3 — Decompression + Entity Integrity
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "CFI-021: Gzip decompression returns correct content")]
    public void Decompress_Gzip_ReturnsCorrectContent()
    {
        const string originalText = "Hello, TurboHttp gzip integration!";
        var compressed = GzipCompress(originalText);

        var decompressed = ContentEncodingDecoder.Decompress(compressed, "gzip");

        Assert.Equal(originalText, Encoding.UTF8.GetString(decompressed));
    }

    [Fact(DisplayName = "CFI-022: Deflate decompression returns correct content")]
    public void Decompress_Deflate_ReturnsCorrectContent()
    {
        const string originalText = "Hello, TurboHttp deflate integration!";
        var compressed = DeflateCompress(originalText);

        var decompressed = ContentEncodingDecoder.Decompress(compressed, "deflate");

        Assert.Equal(originalText, Encoding.UTF8.GetString(decompressed));
    }

    [Fact(DisplayName = "CFI-023: Identity encoding returns body unchanged")]
    public void Decompress_Identity_ReturnBodyUnchanged()
    {
        var body = "plain text body"u8.ToArray();

        var result = ContentEncodingDecoder.Decompress(body, "identity");

        Assert.Equal(body, result);
    }

    [Fact(DisplayName = "CFI-024: Null content-encoding returns body unchanged")]
    public void Decompress_NullEncoding_ReturnBodyUnchanged()
    {
        var body = "raw body"u8.ToArray();

        var result = ContentEncodingDecoder.Decompress(body, null);

        Assert.Equal(body, result);
    }

    [Fact(DisplayName = "CFI-025: Empty body with gzip encoding returns empty bytes")]
    public void Decompress_EmptyBody_GzipEncoding_ReturnsEmpty()
    {
        // Empty body: ContentEncodingDecoder skips decompression for 0-length data
        var result = ContentEncodingDecoder.Decompress([], "gzip");

        Assert.Empty(result);
    }

    [Fact(DisplayName = "CFI-026: Unknown encoding throws HttpDecoderException with DecompressionFailed")]
    public void Decompress_UnknownEncoding_ThrowsHttpDecoderException()
    {
        var body = "some data"u8.ToArray();

        var ex = Assert.Throws<HttpDecoderException>(() => ContentEncodingDecoder.Decompress(body, "zstd"));

        Assert.Equal(HttpDecodeError.DecompressionFailed, ex.DecodeError);
    }

    [Fact(DisplayName = "CFI-027: Stacked gzip+identity decodes correctly (identity is no-op)")]
    public void Decompress_StackedGzipIdentity_DecodesCorrectly()
    {
        const string originalText = "stacked encoding test";
        var compressed = GzipCompress(originalText);

        // "gzip, identity" — identity is no-op, gzip is applied
        var result = ContentEncodingDecoder.Decompress(compressed, "gzip, identity");

        Assert.Equal(originalText, Encoding.UTF8.GetString(result));
    }

    [Fact(DisplayName = "CFI-028: Decompression does not modify original compressed bytes (immutability)")]
    public void Decompress_DoesNotModifyOriginalBytes()
    {
        const string text = "immutability check";
        var compressed = GzipCompress(text);
        var originalCopy = (byte[])compressed.Clone();

        ContentEncodingDecoder.Decompress(compressed, "gzip");

        Assert.Equal(originalCopy, compressed);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // GROUP 4 — Pooling + Connection Lifecycle (no leaked connections)
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "CFI-031: HTTP/1.1 default — connection reusable after successful response")]
    public void Connection_Http11_Default_IsReusable()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);

        var decision = ConnectionReuseEvaluator.Evaluate(response, HttpVersion.Version11,
            bodyFullyConsumed: true);

        Assert.True(decision.CanReuse);
    }

    [Fact(DisplayName = "CFI-032: Body not fully consumed — connection closed to prevent framing desync")]
    public void Connection_BodyNotConsumed_MustClose()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);

        var decision = ConnectionReuseEvaluator.Evaluate(response, HttpVersion.Version11,
            bodyFullyConsumed: false);

        Assert.False(decision.CanReuse,
            "Connection must be closed when response body was not fully consumed.");
    }

    [Fact(DisplayName = "CFI-033: Protocol error — connection closed (state unknown)")]
    public void Connection_ProtocolError_MustClose()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);

        var decision = ConnectionReuseEvaluator.Evaluate(response, HttpVersion.Version11,
            bodyFullyConsumed: true, protocolErrorOccurred: true);

        Assert.False(decision.CanReuse,
            "Connection must be closed when a protocol error occurred during decoding.");
    }

    [Fact(DisplayName = "CFI-034: Connection: close header — connection must not be reused")]
    public void Connection_CloseHeader_MustNotReuse()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.Connection.Add("close");

        var decision = ConnectionReuseEvaluator.Evaluate(response, HttpVersion.Version11,
            bodyFullyConsumed: true);

        Assert.False(decision.CanReuse,
            "Connection must be closed when server sent 'Connection: close'.");
    }

    [Fact(DisplayName = "CFI-035: Per-host limiter — slot acquired and released correctly")]
    public void PerHostLimiter_AcquireAndRelease_CorrectTracking()
    {
        var limiter = new PerHostConnectionLimiter(2);

        var acquired1 = limiter.TryAcquire("example.com");
        var acquired2 = limiter.TryAcquire("example.com");
        var acquired3 = limiter.TryAcquire("example.com"); // should fail — at limit

        Assert.True(acquired1);
        Assert.True(acquired2);
        Assert.False(acquired3);
        Assert.Equal(2, limiter.GetActiveConnections("example.com"));

        limiter.Release("example.com");
        Assert.Equal(1, limiter.GetActiveConnections("example.com"));

        var acquired4 = limiter.TryAcquire("example.com"); // should succeed now
        Assert.True(acquired4);
    }

    [Fact(DisplayName = "CFI-036: Per-host limiter — release brings count to zero cleanly")]
    public void PerHostLimiter_Release_BringsCountToZero()
    {
        var limiter = new PerHostConnectionLimiter(3);

        limiter.TryAcquire("api.example.com");
        limiter.TryAcquire("api.example.com");
        limiter.Release("api.example.com");
        limiter.Release("api.example.com");

        Assert.Equal(0, limiter.GetActiveConnections("api.example.com"));

        // After zero, a fresh acquire must succeed
        var acquired = limiter.TryAcquire("api.example.com");
        Assert.True(acquired);
    }

    [Fact(DisplayName = "CFI-037: HTTP/2 connection always reusable regardless of body state")]
    public void Connection_Http2_AlwaysReusable()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);

        // HTTP/2 uses multiplexed streams; body state and protocol errors at the stream
        // layer do not affect connection reuse.
        var decision = ConnectionReuseEvaluator.Evaluate(response, HttpVersion.Version20,
            bodyFullyConsumed: false, protocolErrorOccurred: true);

        Assert.True(decision.CanReuse,
            "HTTP/2 connection must always be reusable (multiplexed streams).");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // GROUP 5 — Retry + Streaming
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "CFI-041: GET with partially consumed body — NoRetry (cannot rewind)")]
    public void Retry_Get_PartiallyConsumedBody_NoRetry()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");

        var decision = RetryEvaluator.Evaluate(request,
            networkFailure: true,
            bodyPartiallyConsumed: true);

        Assert.False(decision.ShouldRetry,
            "Must not retry when body was partially consumed (cannot rewind).");
    }

    [Fact(DisplayName = "CFI-042: GET with rewindable body on network failure — Retry")]
    public void Retry_Get_RewindableBody_NetworkFailure_ShouldRetry()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");

        var decision = RetryEvaluator.Evaluate(request,
            networkFailure: true,
            bodyPartiallyConsumed: false);

        Assert.True(decision.ShouldRetry,
            "Idempotent GET with rewindable body must be retried on network failure.");
    }

    [Fact(DisplayName = "CFI-043: POST — NoRetry regardless of failure type")]
    public void Retry_Post_NonIdempotent_NoRetry()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/submit")
        {
            Content = new StringContent("data")
        };

        var decision = RetryEvaluator.Evaluate(request,
            networkFailure: true,
            bodyPartiallyConsumed: false);

        Assert.False(decision.ShouldRetry,
            "POST is non-idempotent and must never be automatically retried.");
    }

    [Fact(DisplayName = "CFI-044: Streamed GET body (partial) on 408 — NoRetry")]
    public void Retry_Get_StreamedBody_On408_NoRetry()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        var response = new HttpResponseMessage((HttpStatusCode)408);

        var decision = RetryEvaluator.Evaluate(request,
            response: response,
            bodyPartiallyConsumed: true);

        Assert.False(decision.ShouldRetry,
            "Must not retry 408 when body was partially consumed.");
    }

    [Fact(DisplayName = "CFI-045: Rewindable GET on 408 — Retry")]
    public void Retry_Get_RewindableBody_On408_ShouldRetry()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        var response = new HttpResponseMessage((HttpStatusCode)408);

        var decision = RetryEvaluator.Evaluate(request,
            response: response,
            bodyPartiallyConsumed: false,
            attemptCount: 1);

        Assert.True(decision.ShouldRetry,
            "Idempotent GET must be retried on 408 when body is rewindable.");
    }

    [Fact(DisplayName = "CFI-046: MaxRetries exhausted — NoRetry even for idempotent GET")]
    public void Retry_MaxRetriesExhausted_NoRetry()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        var policy = new RetryPolicy { MaxRetries = 3 };

        // attemptCount == MaxRetries → no more retries
        var decision = RetryEvaluator.Evaluate(request,
            networkFailure: true,
            bodyPartiallyConsumed: false,
            attemptCount: 3,
            policy: policy);

        Assert.False(decision.ShouldRetry,
            "Must not retry when the attempt count has reached MaxRetries.");
    }

    [Fact(DisplayName = "CFI-047: Retry-After delay extracted and surfaced in decision")]
    public void Retry_RetryAfterHeader_Extracted()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        var response = new HttpResponseMessage((HttpStatusCode)503);
        response.Headers.TryAddWithoutValidation("Retry-After", "30");

        var decision = RetryEvaluator.Evaluate(request,
            response: response,
            attemptCount: 1);

        Assert.True(decision.ShouldRetry);
        Assert.NotNull(decision.RetryAfterDelay);
        Assert.Equal(TimeSpan.FromSeconds(30), decision.RetryAfterDelay!.Value);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // GROUP 6 — HEAD never exposes body
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "CFI-051: HEAD decoded via TryDecodeHead — body is always empty")]
    public async Task Head_TryDecodeHead_BodyAlwaysEmpty()
    {
        // Craft a response that has Content-Length and a body section
        const string rawResponse =
            "HTTP/1.1 200 OK\r\n" +
            "Content-Type: text/plain\r\n" +
            "Content-Length: 13\r\n" +
            "\r\n" +
            "Hello, World!";

        var decoder = new Http11Decoder();
        var bytes = Encoding.ASCII.GetBytes(rawResponse).AsMemory();

        var ok = decoder.TryDecodeHead(bytes, out var responses);

        Assert.True(ok);
        Assert.Single(responses);
        var body = await responses[0].Content.ReadAsByteArrayAsync();
        Assert.Empty(body);
    }

    [Fact(DisplayName = "CFI-052: HEAD response with large Content-Length — body still empty", Timeout = 10000)]
    public async Task Head_LargeContentLength_BodyStillEmpty()
    {
        const string rawResponse =
            "HTTP/1.1 200 OK\r\n" +
            "Content-Type: application/octet-stream\r\n" +
            "Content-Length: 1048576\r\n" +
            "\r\n";
        // No body bytes follow; TryDecodeHead does not need them

        var decoder = new Http11Decoder();
        var bytes = Encoding.ASCII.GetBytes(rawResponse).AsMemory();

        var ok = decoder.TryDecodeHead(bytes, out var responses);

        Assert.True(ok);
        Assert.Single(responses);
        var body = await responses[0].Content.ReadAsByteArrayAsync();
        Assert.Empty(body);
    }

    [Fact(DisplayName = "CFI-053: HEAD response with Content-Encoding — headers preserved, body empty",
        Timeout = 10000)]
    public async Task Head_ContentEncoding_HeadersPreserved_BodyEmpty()
    {
        const string rawResponse =
            "HTTP/1.1 200 OK\r\n" +
            "Content-Encoding: gzip\r\n" +
            "Content-Length: 512\r\n" +
            "\r\n";

        var decoder = new Http11Decoder();
        var bytes = Encoding.ASCII.GetBytes(rawResponse).AsMemory();

        var ok = decoder.TryDecodeHead(bytes, out var responses);

        Assert.True(ok);
        Assert.Single(responses);

        // Content-Encoding header is preserved (metadata), but body is empty
        var response = responses[0];
        var body = await responses[0].Content.ReadAsByteArrayAsync();
        Assert.Empty(body);

        // Content-Encoding header should be on the content headers
        var encodingValues = response.Content.Headers.ContentEncoding;
        Assert.Contains("gzip", encodingValues);
    }

    [Fact(DisplayName = "CFI-054: HEAD method is idempotent — RetryEvaluator allows retry")]
    public void Head_IsIdempotent_RetryAllowed()
    {
        var request = new HttpRequestMessage(HttpMethod.Head, "http://example.com/resource");

        var decision = RetryEvaluator.Evaluate(request,
            networkFailure: true,
            bodyPartiallyConsumed: false,
            attemptCount: 1);

        Assert.True(decision.ShouldRetry,
            "HEAD is idempotent and must be allowed to retry on network failure.");
    }

    [Fact(DisplayName = "CFI-055: HEAD redirect with 307 preserves HEAD method")]
    public void Head_Redirect307_MethodPreserved()
    {
        var original = new HttpRequestMessage(HttpMethod.Head, "http://example.com/resource");
        var redirectResponse = BuildRedirect(HttpStatusCode.TemporaryRedirect, "http://example.com/new-resource");

        var handler = new RedirectHandler();
        var newRequest = handler.BuildRedirectRequest(original, redirectResponse);

        Assert.Equal(HttpMethod.Head, newRequest.Method);
    }

    [Fact(DisplayName = "CFI-056: HEAD ConnectionReuseEvaluator — connection reusable after HEAD")]
    public void Head_ConnectionReuse_IsReusable()
    {
        // HEAD response body is always empty by definition, so it is "fully consumed"
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.TryAddWithoutValidation("Content-Length", "1024"); // metadata only

        var decision = ConnectionReuseEvaluator.Evaluate(response, HttpVersion.Version11,
            bodyFullyConsumed: true); // HEAD bodies are trivially consumed (they are empty)

        Assert.True(decision.CanReuse,
            "Connection must be reusable after a successful HEAD response.");
    }
}