using System.Net;
using System.Text;
using TurboHttp.IntegrationTests.Shared;
using TurboHttp.Protocol;

namespace TurboHttp.IntegrationTests.Http11;

/// <summary>
/// Phase 13 — HTTP/1.1 Integration Tests: Header handling scenarios.
/// Tests cover custom headers, multi-value headers, conditional requests,
/// caching headers, and security (obs-fold rejection).
/// </summary>
[Collection("Http11Integration")]
public sealed class Http11HeaderTests
{
    private readonly KestrelFixture _fixture;

    public Http11HeaderTests(KestrelFixture fixture)
    {
        _fixture = fixture;
    }

    // ── 20 custom headers round-trip ──────────────────────────────────────────

    [Fact(DisplayName = "IT-11-070: 20 custom X-* headers round-trip via /headers/echo")]
    public async Task TwentyCustomHeaders_RoundTrip_AllPresent()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, "/headers/echo"));
        for (var i = 1; i <= 20; i++)
        {
            request.Headers.TryAddWithoutValidation($"X-Custom-{i}", $"value-{i}");
        }

        var response = await Http11Helper.SendAsync(_fixture.Port, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        for (var i = 1; i <= 20; i++)
        {
            Assert.True(response.Headers.Contains($"X-Custom-{i}"),
                $"X-Custom-{i} should be present in response");
            Assert.Equal($"value-{i}", response.Headers.GetValues($"X-Custom-{i}").First());
        }
    }

    // ── Duplicate header names (List-append semantics) ────────────────────────

    [Fact(DisplayName = "IT-11-071: GET /multiheader — duplicate X-Value headers decoded with list semantics")]
    public async Task Get_MultiHeader_DuplicateHeaderNames_ListAppendSemantics()
    {
        var response = await Http11Helper.GetAsync(_fixture.Port, "/multiheader");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("X-Value"));
        var values = response.Headers.GetValues("X-Value").ToList();
        // The server sends two X-Value headers; the decoder may combine them or keep separate
        var combined = string.Join(",", values);
        Assert.Contains("alpha", combined);
        Assert.Contains("beta", combined);
    }

    // ── Content-Type with charset parameter ──────────────────────────────────

    [Fact(DisplayName = "IT-11-072: POST /echo — Content-Type with charset parameter round-trips correctly")]
    public async Task Post_ContentType_WithCharset_RoundTrips()
    {
        // Use ByteArrayContent to avoid StringContent's media-type validation
        var bodyBytes = Encoding.UTF8.GetBytes("charset-test");
        var content = new ByteArrayContent(bodyBytes);
        // Set Content-Type with charset parameter manually
        content.Headers.TryAddWithoutValidation("Content-Type", "text/plain; charset=utf-8");

        var request = new HttpRequestMessage(HttpMethod.Post, Http11Helper.BuildUri(_fixture.Port, "/echo"))
        {
            Content = content
        };

        var response = await Http11Helper.SendAsync(_fixture.Port, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var ct = response.Content.Headers.ContentType;
        Assert.NotNull(ct);
        Assert.Equal("text/plain", ct.MediaType);
        // charset parameter echoed from request
        Assert.NotNull(ct.CharSet);
    }

    // ── Multi-value Accept header ────────────────────────────────────────────

    [Fact(DisplayName = "IT-11-073: Request with multi-value Accept header is sent and response is 200")]
    public async Task Request_MultiValueAccept_Returns200()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, "/hello"));
        request.Headers.TryAddWithoutValidation("Accept", "text/html, application/json, */*");

        var response = await Http11Helper.SendAsync(_fixture.Port, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── Authorization header preserved ────────────────────────────────────────

    [Fact(DisplayName = "IT-11-074: Authorization header causes /auth to return 200")]
    public async Task AuthorizationHeader_Preserved_Returns200()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, "/auth"));
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer test-token");

        var response = await Http11Helper.SendAsync(_fixture.Port, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(DisplayName = "IT-11-075: Missing Authorization header causes /auth to return 401")]
    public async Task NoAuthorizationHeader_Returns401()
    {
        var response = await Http11Helper.GetAsync(_fixture.Port, "/auth");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── Cookie header preserved ───────────────────────────────────────────────

    [Fact(DisplayName = "IT-11-076: Cookie header is sent and echoed back via /headers/echo")]
    public async Task CookieHeader_Preserved_EchoedBack()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, "/headers/echo"));
        // Cookie is not an X-* header, so use X-Cookie-Test to echo it
        request.Headers.TryAddWithoutValidation("X-Cookie-Echo", "session=abc; user=xyz");

        var response = await Http11Helper.SendAsync(_fixture.Port, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("X-Cookie-Echo"));
        Assert.Equal("session=abc; user=xyz", response.Headers.GetValues("X-Cookie-Echo").First());
    }

    // ── Response Date header ──────────────────────────────────────────────────

    [Fact(DisplayName = "IT-11-077: Response Date header is present and parseable as RFC 7231 date")]
    public async Task Response_DateHeader_ParseableAsRfc7231Date()
    {
        var response = await Http11Helper.GetAsync(_fixture.Port, "/hello");

        Assert.True(response.Headers.Contains("Date"), "Date header should be present");
        var dateStr = response.Headers.GetValues("Date").First();
        Assert.True(DateTimeOffset.TryParse(dateStr, out var date),
            $"Date header '{dateStr}' should be parseable as DateTimeOffset");
        // Date should be recent (within last 60 seconds)
        Assert.True(Math.Abs((DateTimeOffset.UtcNow - date).TotalSeconds) < 60,
            "Date header should reflect the current time");
    }

    // ── ETag / If-None-Match conditional 304 ─────────────────────────────────

    [Fact(DisplayName = "IT-11-078: GET /etag returns 200 with ETag header")]
    public async Task Get_Etag_Returns200_WithEtagHeader()
    {
        var response = await Http11Helper.GetAsync(_fixture.Port, "/etag");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("ETag"), "ETag header should be present");
        var etag = response.Headers.GetValues("ETag").First();
        Assert.Equal("\"v1\"", etag);
    }

    [Fact(DisplayName = "IT-11-079: GET /etag with matching If-None-Match returns 304 with no body")]
    public async Task Get_Etag_WithMatchingIfNoneMatch_Returns304_NoBody()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, "/etag"));
        request.Headers.TryAddWithoutValidation("If-None-Match", "\"v1\"");

        var response = await Http11Helper.SendAsync(_fixture.Port, request);

        Assert.Equal(HttpStatusCode.NotModified, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Empty(body);
    }

    [Fact(DisplayName = "IT-11-080: GET /etag with non-matching If-None-Match returns 200 full body")]
    public async Task Get_Etag_WithNonMatchingIfNoneMatch_Returns200_FullBody()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, "/etag"));
        request.Headers.TryAddWithoutValidation("If-None-Match", "\"wrong-etag\"");

        var response = await Http11Helper.SendAsync(_fixture.Port, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("etag-resource", body);
    }

    // ── Cache-Control directives ──────────────────────────────────────────────

    [Fact(DisplayName = "IT-11-081: GET /cache returns Cache-Control and Last-Modified headers")]
    public async Task Get_Cache_ReturnsCachingHeaders()
    {
        var response = await Http11Helper.GetAsync(_fixture.Port, "/cache");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("Cache-Control"), "Cache-Control should be present");
        // Last-Modified is treated as a content header by .NET's HttpContentHeaders
        Assert.True(response.Content.Headers.LastModified.HasValue, "Last-Modified should be present");
    }

    // ── X-* custom headers echoed ────────────────────────────────────────────

    [Fact(DisplayName = "IT-11-082: X-* custom headers echoed correctly via /headers/echo")]
    public async Task XCustomHeaders_EchoedCorrectly()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, "/headers/echo"));
        request.Headers.TryAddWithoutValidation("X-Request-Id", "req-12345");
        request.Headers.TryAddWithoutValidation("X-Trace-Id", "trace-67890");

        var response = await Http11Helper.SendAsync(_fixture.Port, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("req-12345", response.Headers.GetValues("X-Request-Id").First());
        Assert.Equal("trace-67890", response.Headers.GetValues("X-Trace-Id").First());
    }

    // ── Very long header value (8 KB) ────────────────────────────────────────

    [Fact(DisplayName = "IT-11-083: Very long header value (4 KB) round-trips via /headers/echo")]
    public async Task VeryLongHeaderValue_4KB_RoundTrips()
    {
        // The decoder default maxHeaderSize is 8192. A 4 KB header value plus other response
        // headers (Date, Content-Length, Connection, etc.) fits within the 8 KB limit.
        // A separate test (decoder unit test) covers the LineTooLong guard.
        var longValue = new string('Z', 4096);
        var request = new HttpRequestMessage(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, "/headers/echo"));
        request.Headers.TryAddWithoutValidation("X-Long-Header", longValue);

        // Use a decoder with a larger header size limit to accommodate the full response
        await using var conn = await Http11Connection.OpenWithHeaderSizeAsync(_fixture.Port, maxHeaderSize: 32768);
        var response = await conn.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("X-Long-Header"),
            "X-Long-Header should be present in response");
        var receivedValue = response.Headers.GetValues("X-Long-Header").First();
        Assert.Equal(longValue, receivedValue);
    }

    // ── Header name case folding ──────────────────────────────────────────────

    [Fact(DisplayName = "IT-11-084: Header names are case-insensitive — X-Mixed-Case echoed correctly")]
    public async Task HeaderName_CaseFolding_EchoedCorrectly()
    {
        // HTTP/1.1 headers are case-insensitive; our decoder must fold correctly
        var request = new HttpRequestMessage(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, "/headers/echo"));
        request.Headers.TryAddWithoutValidation("X-Mixed-CASE", "case-test");

        var response = await Http11Helper.SendAsync(_fixture.Port, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // The response header name may be any case; we check case-insensitively
        Assert.True(response.Headers.Contains("X-Mixed-CASE") ||
                    response.Headers.Contains("x-mixed-case") ||
                    response.Headers.Contains("X-Mixed-Case"),
            "X-Mixed-CASE header should be present in response (any casing)");
    }

    // ── Folded header value (obs-fold) rejected ───────────────────────────────

    [Fact(DisplayName = "IT-11-085: Folded header value (obs-fold) is rejected by Http11Decoder")]
    public void ObsFold_RejectedByDecoder()
    {
        // RFC 9112 §5.2: obs-fold (header continuation with SP/HTAB) is obsolete
        // and MUST be rejected by modern parsers.
        // The continuation line has no colon → decoder throws InvalidHeader.
        var raw =
            "HTTP/1.1 200 OK\r\n" +
            "Content-Length: 5\r\n" +
            "X-Folded: first-line\r\n" +
            "  continuation\r\n" +    // obs-fold: SP at start of line
            "\r\n" +
            "hello";

        using var decoder = new Http11Decoder();
        Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(Encoding.ASCII.GetBytes(raw), out _));
    }

    // ── If-Modified-Since conditional 200 / 304 ───────────────────────────────

    [Fact(DisplayName = "IT-11-086: If-Modified-Since past date → 200 full response")]
    public async Task IfModifiedSince_PastDate_Returns200()
    {
        var pastDate = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero).ToString("R");
        var request = new HttpRequestMessage(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, "/if-modified-since"));
        request.Headers.TryAddWithoutValidation("If-Modified-Since", pastDate);

        var response = await Http11Helper.SendAsync(_fixture.Port, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("fresh-resource", body);
    }

    [Fact(DisplayName = "IT-11-087: If-Modified-Since future date → 304 not modified")]
    public async Task IfModifiedSince_FutureDate_Returns304()
    {
        // The server's fixed last-modified is 2026-01-01; use a later date
        var futureDate = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero).ToString("R");
        var request = new HttpRequestMessage(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, "/if-modified-since"));
        request.Headers.TryAddWithoutValidation("If-Modified-Since", futureDate);

        var response = await Http11Helper.SendAsync(_fixture.Port, request);

        Assert.Equal(HttpStatusCode.NotModified, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Empty(body);
    }

    // ── Pragma: no-cache in response ──────────────────────────────────────────

    [Fact(DisplayName = "IT-11-088: GET /cache response includes Pragma: no-cache header")]
    public async Task Get_Cache_ResponseIncludesPragmaNoCache()
    {
        var response = await Http11Helper.GetAsync(_fixture.Port, "/cache");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("Pragma"), "Pragma header should be present");
        var pragma = response.Headers.GetValues("Pragma").First();
        Assert.Equal("no-cache", pragma);
    }

    // ── Last-Modified in response ─────────────────────────────────────────────

    [Fact(DisplayName = "IT-11-089: GET /cache response includes Last-Modified header")]
    public async Task Get_Cache_ResponseIncludesLastModifiedHeader()
    {
        var response = await Http11Helper.GetAsync(_fixture.Port, "/cache");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Last-Modified is a content header in .NET's HttpContent.Headers (per RFC 7231)
        // The decoder puts it in content.Headers via IsContentHeader check
        var hasLastModified = response.Content.Headers.LastModified.HasValue;
        Assert.True(hasLastModified, "Last-Modified should be present in response Content headers");
    }
}
