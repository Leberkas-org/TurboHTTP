using System.Net;
using TurboHttp.Protocol;

namespace TurboHttp.Tests.RFC9111;

public sealed class ConditionalRequestTests
{
    private static readonly DateTimeOffset _baseTime = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    // ── Helper ───────────────────────────────────────────────────────────────

    private static CacheEntry MakeEntry(string? etag = null, DateTimeOffset? lastModified = null)
    {
        return new CacheEntry
        {
            Response = new HttpResponseMessage(HttpStatusCode.OK),
            Body = System.Text.Encoding.UTF8.GetBytes("cached body"),
            RequestTime = _baseTime.AddSeconds(-1),
            ResponseTime = _baseTime,
            ETag = etag,
            LastModified = lastModified
        };
    }

    // ── BuildConditionalRequest ───────────────────────────────────────────────

    [Fact(DisplayName = "RFC-9111-§4.3.1: entry with ETag adds If-None-Match header")]
    public void ETag_AddsIfNoneMatch()
    {
        var entry = MakeEntry(etag: "\"abc123\"");
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");

        var conditional = CacheValidationRequestBuilder.BuildConditionalRequest(original, entry);

        Assert.True(conditional.Headers.Contains("If-None-Match"));
        var value = string.Join("", conditional.Headers.GetValues("If-None-Match"));
        Assert.Equal("\"abc123\"", value);
    }

    [Fact(DisplayName = "RFC-9111-§4.3.1: entry with Last-Modified adds If-Modified-Since header")]
    public void LastModified_AddsIfModifiedSince()
    {
        var lm = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var entry = MakeEntry(lastModified: lm);
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");

        var conditional = CacheValidationRequestBuilder.BuildConditionalRequest(original, entry);

        Assert.NotNull(conditional.Headers.IfModifiedSince);
        Assert.Equal(lm, conditional.Headers.IfModifiedSince!.Value);
    }

    [Fact(DisplayName = "RFC-9111-§4.3.1: entry with both ETag and Last-Modified adds both headers")]
    public void BothETagAndLastModified_AddsBothHeaders()
    {
        var lm = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var entry = MakeEntry(etag: "\"xyz\"", lastModified: lm);
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");

        var conditional = CacheValidationRequestBuilder.BuildConditionalRequest(original, entry);

        Assert.True(conditional.Headers.Contains("If-None-Match"));
        Assert.NotNull(conditional.Headers.IfModifiedSince);
    }

    [Fact(DisplayName = "RFC-9111-§4.3.1: entry with neither ETag nor Last-Modified adds no conditional headers")]
    public void NoETagNorLastModified_NoConditionalHeaders()
    {
        var entry = MakeEntry();
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");

        var conditional = CacheValidationRequestBuilder.BuildConditionalRequest(original, entry);

        Assert.False(conditional.Headers.Contains("If-None-Match"));
        Assert.Null(conditional.Headers.IfModifiedSince);
    }

    [Fact(DisplayName = "RFC-9111-§4.3.1: conditional request preserves original URI and method")]
    public void ConditionalRequest_PreservesUriAndMethod()
    {
        var entry = MakeEntry(etag: "\"v1\"");
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page");

        var conditional = CacheValidationRequestBuilder.BuildConditionalRequest(original, entry);

        Assert.Equal(original.RequestUri, conditional.RequestUri);
        Assert.Equal(HttpMethod.Get, conditional.Method);
    }

    // ── CanRevalidate ─────────────────────────────────────────────────────────

    [Fact(DisplayName = "RFC-9111-§4.3.2: CanRevalidate returns false for entry without ETag or Last-Modified")]
    public void CanRevalidate_False_WhenNoValidators()
    {
        var entry = MakeEntry();
        Assert.False(CacheValidationRequestBuilder.CanRevalidate(entry));
    }

    [Fact(DisplayName = "RFC-9111-§4.3.2: CanRevalidate returns true when ETag present")]
    public void CanRevalidate_True_WhenETag()
    {
        var entry = MakeEntry(etag: "\"v1\"");
        Assert.True(CacheValidationRequestBuilder.CanRevalidate(entry));
    }

    [Fact(DisplayName = "RFC-9111-§4.3.2: CanRevalidate returns true when Last-Modified present")]
    public void CanRevalidate_True_WhenLastModified()
    {
        var entry = MakeEntry(lastModified: _baseTime);
        Assert.True(CacheValidationRequestBuilder.CanRevalidate(entry));
    }

    // ── MergeNotModifiedResponse ──────────────────────────────────────────────

    [Fact(DisplayName = "RFC-9111-§4.3.4: merged response StatusCode is 200 (not 304)")]
    public void MergeNotModified_StatusCode_Is200()
    {
        var entry = MakeEntry(etag: "\"v1\"");
        var notModified = new HttpResponseMessage(HttpStatusCode.NotModified);

        var merged = CacheValidationRequestBuilder.MergeNotModifiedResponse(notModified, entry);

        Assert.Equal(HttpStatusCode.OK, merged.StatusCode);
    }

    [Fact(DisplayName = "RFC-9111-§4.3.4: merged response body is the cached body")]
    public async Task MergeNotModified_Body_IsCachedBody()
    {
        var entry = MakeEntry(etag: "\"v1\"");
        var notModified = new HttpResponseMessage(HttpStatusCode.NotModified);

        var merged = CacheValidationRequestBuilder.MergeNotModifiedResponse(notModified, entry);

        var body = await merged.Content.ReadAsByteArrayAsync();
        Assert.Equal(entry.Body, body);
    }

    [Fact(DisplayName = "RFC-9111-§4.3.4: 304 ETag header overrides cached ETag in merged response")]
    public void MergeNotModified_NewHeaderOverridesCached()
    {
        var entry = MakeEntry(etag: "\"old\"");
        var notModified = new HttpResponseMessage(HttpStatusCode.NotModified);
        notModified.Headers.TryAddWithoutValidation("ETag", "\"new\"");

        var merged = CacheValidationRequestBuilder.MergeNotModifiedResponse(notModified, entry);

        var etag = string.Join("", merged.Headers.GetValues("ETag"));
        Assert.Equal("\"new\"", etag);
    }

    [Fact(DisplayName = "RFC-9111-§4.3.4: merged response preserves cached response version")]
    public void MergeNotModified_PreservesVersion()
    {
        var entry = MakeEntry();
        entry.Response.Version = new Version(2, 0);
        var notModified = new HttpResponseMessage(HttpStatusCode.NotModified);

        var merged = CacheValidationRequestBuilder.MergeNotModifiedResponse(notModified, entry);

        Assert.Equal(new Version(2, 0), merged.Version);
    }
}
