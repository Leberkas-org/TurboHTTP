using System.Net;
using TurboHttp.Protocol.RFC9111;

namespace TurboHttp.Tests.RFC9111;

/// <summary>
/// RFC 9111 §4.3 — Cache validation and conditional request tests.
/// Covers If-None-Match and If-Modified-Since header injection,
/// 304 Not Modified response merging, and stale-entry revalidation.
/// </summary>
/// <remarks>
/// Class under test: <see cref="CacheValidationRequestBuilder"/>.
/// RFC 9111 §4.3: Stale cached responses may be revalidated using conditional requests.
/// </remarks>
public sealed class ConditionalRequestTests
{
    private static readonly DateTimeOffset _baseTime = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);


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


    [Fact(DisplayName = "RFC9111-4.3.1-CR-001: entry with ETag adds If-None-Match header")]
    public void Should_AddIfNoneMatchHeader_When_EntryHasETag()
    {
        var entry = MakeEntry(etag: "\"abc123\"");
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");

        var conditional = CacheValidationRequestBuilder.BuildConditionalRequest(original, entry);

        Assert.True(conditional.Headers.Contains("If-None-Match"));
        var value = string.Join("", conditional.Headers.GetValues("If-None-Match"));
        Assert.Equal("\"abc123\"", value);
    }

    [Fact(DisplayName = "RFC9111-4.3.1-CR-002: entry with Last-Modified adds If-Modified-Since header")]
    public void Should_AddIfModifiedSinceHeader_When_EntryHasLastModified()
    {
        var lm = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var entry = MakeEntry(lastModified: lm);
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");

        var conditional = CacheValidationRequestBuilder.BuildConditionalRequest(original, entry);

        Assert.NotNull(conditional.Headers.IfModifiedSince);
        Assert.Equal(lm, conditional.Headers.IfModifiedSince!.Value);
    }

    [Fact(DisplayName = "RFC9111-4.3.1-CR-003: entry with both ETag and Last-Modified adds both headers")]
    public void Should_AddBothConditionalHeaders_When_EntryHasETagAndLastModified()
    {
        var lm = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var entry = MakeEntry(etag: "\"xyz\"", lastModified: lm);
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");

        var conditional = CacheValidationRequestBuilder.BuildConditionalRequest(original, entry);

        Assert.True(conditional.Headers.Contains("If-None-Match"));
        Assert.NotNull(conditional.Headers.IfModifiedSince);
    }

    [Fact(DisplayName = "RFC9111-4.3.1-CR-004: entry with neither ETag nor Last-Modified adds no conditional headers")]
    public void Should_AddNoConditionalHeaders_When_EntryHasNoValidators()
    {
        var entry = MakeEntry();
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");

        var conditional = CacheValidationRequestBuilder.BuildConditionalRequest(original, entry);

        Assert.False(conditional.Headers.Contains("If-None-Match"));
        Assert.Null(conditional.Headers.IfModifiedSince);
    }

    [Fact(DisplayName = "RFC9111-4.3.1-CR-005: conditional request preserves original URI and method")]
    public void Should_PreserveUriAndMethod_When_BuildingConditionalRequest()
    {
        var entry = MakeEntry(etag: "\"v1\"");
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page");

        var conditional = CacheValidationRequestBuilder.BuildConditionalRequest(original, entry);

        Assert.Equal(original.RequestUri, conditional.RequestUri);
        Assert.Equal(HttpMethod.Get, conditional.Method);
    }


    [Fact(DisplayName = "RFC9111-4.3.2-CR-006: CanRevalidate returns false for entry without ETag or Last-Modified")]
    public void Should_ReturnFalse_When_NoValidatorsPresent()
    {
        var entry = MakeEntry();
        Assert.False(CacheValidationRequestBuilder.CanRevalidate(entry));
    }

    [Fact(DisplayName = "RFC9111-4.3.2-CR-007: CanRevalidate returns true when ETag present")]
    public void Should_ReturnTrue_When_ETagPresent()
    {
        var entry = MakeEntry(etag: "\"v1\"");
        Assert.True(CacheValidationRequestBuilder.CanRevalidate(entry));
    }

    [Fact(DisplayName = "RFC9111-4.3.2-CR-008: CanRevalidate returns true when Last-Modified present")]
    public void Should_ReturnTrue_When_LastModifiedPresent()
    {
        var entry = MakeEntry(lastModified: _baseTime);
        Assert.True(CacheValidationRequestBuilder.CanRevalidate(entry));
    }


    [Fact(DisplayName = "RFC9111-4.3.4-CR-009: merged response StatusCode is 200 (not 304)")]
    public void Should_Return200StatusCode_When_MergingNotModifiedResponse()
    {
        var entry = MakeEntry(etag: "\"v1\"");
        var notModified = new HttpResponseMessage(HttpStatusCode.NotModified);

        var merged = CacheValidationRequestBuilder.MergeNotModifiedResponse(notModified, entry);

        Assert.Equal(HttpStatusCode.OK, merged.StatusCode);
    }

    [Fact(DisplayName = "RFC9111-4.3.4-CR-010: merged response body is the cached body")]
    public async Task Should_ReturnCachedBody_When_MergingNotModifiedResponse()
    {
        var entry = MakeEntry(etag: "\"v1\"");
        var notModified = new HttpResponseMessage(HttpStatusCode.NotModified);

        var merged = CacheValidationRequestBuilder.MergeNotModifiedResponse(notModified, entry);

        var body = await merged.Content.ReadAsByteArrayAsync();
        Assert.Equal(entry.Body, body);
    }

    [Fact(DisplayName = "RFC9111-4.3.4-CR-011: 304 ETag header overrides cached ETag in merged response")]
    public void Should_OverrideCachedETag_When_304HasNewETagHeader()
    {
        var entry = MakeEntry(etag: "\"old\"");
        var notModified = new HttpResponseMessage(HttpStatusCode.NotModified);
        notModified.Headers.TryAddWithoutValidation("ETag", "\"new\"");

        var merged = CacheValidationRequestBuilder.MergeNotModifiedResponse(notModified, entry);

        var etag = string.Join("", merged.Headers.GetValues("ETag"));
        Assert.Equal("\"new\"", etag);
    }

    [Fact(DisplayName = "RFC9111-4.3.4-CR-012: merged response preserves cached response version")]
    public void Should_PreserveVersion_When_MergingNotModifiedResponse()
    {
        var entry = MakeEntry();
        entry.Response.Version = new Version(2, 0);
        var notModified = new HttpResponseMessage(HttpStatusCode.NotModified);

        var merged = CacheValidationRequestBuilder.MergeNotModifiedResponse(notModified, entry);

        Assert.Equal(new Version(2, 0), merged.Version);
    }
}
