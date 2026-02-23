using System.Net;
using TurboHttp.IntegrationTests.Shared;

namespace TurboHttp.IntegrationTests.Http11;

/// <summary>
/// Phase 14 — HTTP/1.1 Integration Tests: Caching and conditional requests.
/// Tests cover If-None-Match, If-Modified-Since, Cache-Control, ETag, Last-Modified,
/// Expires, and Pragma headers.
/// </summary>
[Collection("Http11Integration")]
public sealed class Http11CachingTests
{
    private readonly KestrelFixture _fixture;

    public Http11CachingTests(KestrelFixture fixture)
    {
        _fixture = fixture;
    }

    // ── If-None-Match matches ETag → 304 ─────────────────────────────────────

    [Fact(DisplayName = "IT-11A-030: If-None-Match matches ETag — server returns 304 with no body")]
    public async Task IfNoneMatch_MatchesETag_Returns304_NoBody()
    {
        const string etag = "\"v1\"";
        var request = new HttpRequestMessage(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, "/etag"));
        request.Headers.TryAddWithoutValidation("If-None-Match", etag);

        var response = await Http11Helper.SendAsync(_fixture.Port, request);

        Assert.Equal(HttpStatusCode.NotModified, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Empty(body);
    }

    // ── If-None-Match no match → 200 full body ────────────────────────────────

    [Fact(DisplayName = "IT-11A-031: If-None-Match no match — server returns 200 with full body")]
    public async Task IfNoneMatch_NoMatch_Returns200_FullBody()
    {
        const string staleEtag = "\"old-version\"";
        var request = new HttpRequestMessage(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, "/etag"));
        request.Headers.TryAddWithoutValidation("If-None-Match", staleEtag);

        var response = await Http11Helper.SendAsync(_fixture.Port, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("etag-resource", body);
    }

    // ── ETag format valid (quoted string) ────────────────────────────────────

    [Fact(DisplayName = "IT-11A-032: ETag in 200 response is a valid quoted-string")]
    public async Task ETag_InResponse_IsValidQuotedString()
    {
        var response = await Http11Helper.GetAsync(_fixture.Port, "/etag");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var etag = response.Headers.ETag;
        Assert.NotNull(etag);
        // RFC 7232 §2.3: ETag = DQUOTE *etagc DQUOTE | "W/" DQUOTE *etagc DQUOTE
        Assert.True(etag!.Tag.StartsWith("\"") && etag.Tag.EndsWith("\""),
            $"ETag '{etag.Tag}' must be a quoted string");
    }

    // ── If-Modified-Since past → 200 ─────────────────────────────────────────

    [Fact(DisplayName = "IT-11A-033: If-Modified-Since with past date — server returns 200 with full body")]
    public async Task IfModifiedSince_PastDate_Returns200()
    {
        // /if-modified-since uses a fixed date of 2026-01-01
        // A date before that should result in 200 (resource is newer)
        var pastDate = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero).ToString("R");
        var request = new HttpRequestMessage(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, "/if-modified-since"));
        request.Headers.TryAddWithoutValidation("If-Modified-Since", pastDate);

        var response = await Http11Helper.SendAsync(_fixture.Port, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("fresh-resource", body);
    }

    // ── If-Modified-Since future → 304 ───────────────────────────────────────

    [Fact(DisplayName = "IT-11A-034: If-Modified-Since with future date — server returns 304 Not Modified")]
    public async Task IfModifiedSince_FutureDate_Returns304()
    {
        // A date equal to or after the fixed last-modified (2026-01-01) → 304
        var futureDate = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero).ToString("R");
        var request = new HttpRequestMessage(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, "/if-modified-since"));
        request.Headers.TryAddWithoutValidation("If-Modified-Since", futureDate);

        var response = await Http11Helper.SendAsync(_fixture.Port, request);

        Assert.Equal(HttpStatusCode.NotModified, response.StatusCode);
    }

    // ── Last-Modified in response ─────────────────────────────────────────────

    [Fact(DisplayName = "IT-11A-035: Last-Modified header present in response from /if-modified-since")]
    public async Task LastModified_InResponse_Present()
    {
        var response = await Http11Helper.GetAsync(_fixture.Port, "/if-modified-since");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Content.Headers.LastModified.HasValue,
            "Last-Modified content header should be present");
    }

    [Fact(DisplayName = "IT-11A-036: Last-Modified date is parseable RFC 7231 date")]
    public async Task LastModified_IsParseable_Rfc7231Date()
    {
        var response = await Http11Helper.GetAsync(_fixture.Port, "/if-modified-since");

        var lastModified = response.Content.Headers.LastModified;
        Assert.True(lastModified.HasValue, "Last-Modified should be present");
        // DateTimeOffset value should be the fixed date 2026-01-01
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), lastModified!.Value);
    }

    // ── Cache-Control: no-cache in request ───────────────────────────────────

    [Fact(DisplayName = "IT-11A-037: Cache-Control: no-cache request header sent — server still returns 200")]
    public async Task CacheControl_NoCacheRequest_ServerReturns200()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, "/cache"));
        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue
        {
            NoCache = true
        };

        var response = await Http11Helper.SendAsync(_fixture.Port, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── Cache-Control: max-age=0 in request ──────────────────────────────────

    [Fact(DisplayName = "IT-11A-038: Cache-Control: max-age=0 request header sent — server still returns 200")]
    public async Task CacheControl_MaxAge0_ServerReturns200()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, "/cache"));
        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue
        {
            MaxAge = TimeSpan.Zero
        };

        var response = await Http11Helper.SendAsync(_fixture.Port, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── Response Cache-Control: no-store ─────────────────────────────────────

    [Fact(DisplayName = "IT-11A-039: GET /cache/no-store — response Cache-Control contains no-store")]
    public async Task Get_CacheNoStore_ResponseHasNoCacheControl_NoStore()
    {
        var response = await Http11Helper.GetAsync(_fixture.Port, "/cache/no-store");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cacheControl = response.Headers.CacheControl;
        Assert.NotNull(cacheControl);
        Assert.True(cacheControl!.NoStore, "Cache-Control: no-store should be set");
    }

    // ── Cache-Control in response (max-age, public) ───────────────────────────

    [Fact(DisplayName = "IT-11A-040: GET /cache — response Cache-Control max-age and public directives present")]
    public async Task Get_Cache_ResponseHasCacheControlDirectives()
    {
        var response = await Http11Helper.GetAsync(_fixture.Port, "/cache");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cacheControl = response.Headers.CacheControl;
        Assert.NotNull(cacheControl);
        Assert.True(cacheControl!.MaxAge.HasValue, "Cache-Control should have max-age");
        Assert.Equal(TimeSpan.FromHours(1), cacheControl.MaxAge);
        Assert.True(cacheControl.Public, "Cache-Control should have public directive");
    }

    // ── Expires header in response ────────────────────────────────────────────

    [Fact(DisplayName = "IT-11A-041: GET /cache — response Expires header present and in the future")]
    public async Task Get_Cache_ResponseHasExpires_InFuture()
    {
        var response = await Http11Helper.GetAsync(_fixture.Port, "/cache");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var expires = response.Content.Headers.Expires;
        Assert.True(expires.HasValue, "Expires header should be present");
        Assert.True(expires!.Value > DateTimeOffset.UtcNow, "Expires should be in the future");
    }

    // ── Pragma: no-cache ──────────────────────────────────────────────────────

    [Fact(DisplayName = "IT-11A-042: GET /cache — response Pragma: no-cache header present")]
    public async Task Get_Cache_ResponseHasPragmaNoCache()
    {
        var response = await Http11Helper.GetAsync(_fixture.Port, "/cache");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Pragma is a general header — check via response headers
        Assert.True(response.Headers.Contains("Pragma"),
            "Pragma header should be present");
        var pragma = response.Headers.GetValues("Pragma").FirstOrDefault();
        Assert.Equal("no-cache", pragma);
    }

    // ── ETag round-trip ───────────────────────────────────────────────────────

    [Fact(DisplayName = "IT-11A-043: ETag from 200 response used in next If-None-Match — returns 304")]
    public async Task ETag_RoundTrip_200ThenConditional304()
    {
        // First request: get resource and its ETag
        var firstResponse = await Http11Helper.GetAsync(_fixture.Port, "/etag");
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        var etag = firstResponse.Headers.ETag?.Tag;
        Assert.NotNull(etag);

        // Second request: use ETag in If-None-Match → expect 304
        var conditionalRequest = new HttpRequestMessage(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, "/etag"));
        conditionalRequest.Headers.TryAddWithoutValidation("If-None-Match", etag!);
        var secondResponse = await Http11Helper.SendAsync(_fixture.Port, conditionalRequest);

        Assert.Equal(HttpStatusCode.NotModified, secondResponse.StatusCode);
    }

    // ── Last-Modified and If-Modified-Since round-trip ───────────────────────

    [Fact(DisplayName = "IT-11A-044: If-Modified-Since with Last-Modified date → 304 (resource not changed)")]
    public async Task IfModifiedSince_WithLastModifiedDate_Returns304()
    {
        // Use the fixed Last-Modified date directly as the If-Modified-Since value
        var lastModified = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).ToString("R");
        var request = new HttpRequestMessage(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, "/if-modified-since"));
        request.Headers.TryAddWithoutValidation("If-Modified-Since", lastModified);

        var response = await Http11Helper.SendAsync(_fixture.Port, request);

        Assert.Equal(HttpStatusCode.NotModified, response.StatusCode);
    }
}
