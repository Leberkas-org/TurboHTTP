using System.Net;
using TurboHTTP.Protocol.Caching;

namespace TurboHTTP.Tests.Caching;

/// <summary>
/// RFC 9111 §4.3 — Cache validation and conditional request tests.
/// Covers If-None-Match and If-Modified-Since header injection,
/// 304 Not Modified response merging, and stale-entry revalidation.
/// </summary>
/// <remarks>
/// Class under test: <see cref="CacheValidationRequestBuilder"/>.
/// RFC 9111 §4.3: Stale cached responses may be revalidated using conditional requests.
/// </remarks>
public sealed class CacheValidationSpec
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


    [Trait("RFC", "RFC9111-4.3.1")]
    [Fact(Timeout = 5000)]
    public void CacheValidation_should_add_if_none_match_header_when_entry_has_etag()
    {
        var entry = MakeEntry(etag: "\"abc123\"");
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");

        var conditional = CacheValidationRequestBuilder.BuildConditionalRequest(original, entry);

        Assert.True(conditional.Headers.Contains("If-None-Match"));
        var value = string.Join("", conditional.Headers.GetValues("If-None-Match"));
        Assert.Equal("\"abc123\"", value);
    }

    [Trait("RFC", "RFC9111-4.3.1")]
    [Fact(Timeout = 5000)]
    public void CacheValidation_should_add_if_modified_since_header_when_entry_has_last_modified()
    {
        var lm = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var entry = MakeEntry(lastModified: lm);
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");

        var conditional = CacheValidationRequestBuilder.BuildConditionalRequest(original, entry);

        Assert.NotNull(conditional.Headers.IfModifiedSince);
        Assert.Equal(lm, conditional.Headers.IfModifiedSince!.Value);
    }

    [Trait("RFC", "RFC9111-4.3.1")]
    [Fact(Timeout = 5000)]
    public void CacheValidation_should_add_both_conditional_headers_when_entry_has_etag_and_last_modified()
    {
        var lm = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var entry = MakeEntry(etag: "\"xyz\"", lastModified: lm);
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");

        var conditional = CacheValidationRequestBuilder.BuildConditionalRequest(original, entry);

        Assert.True(conditional.Headers.Contains("If-None-Match"));
        Assert.NotNull(conditional.Headers.IfModifiedSince);
    }

    [Trait("RFC", "RFC9111-4.3.1")]
    [Fact(Timeout = 5000)]
    public void CacheValidation_should_add_no_conditional_headers_when_entry_has_no_validators()
    {
        var entry = MakeEntry();
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");

        var conditional = CacheValidationRequestBuilder.BuildConditionalRequest(original, entry);

        Assert.False(conditional.Headers.Contains("If-None-Match"));
        Assert.Null(conditional.Headers.IfModifiedSince);
    }

    [Trait("RFC", "RFC9111-4.3.1")]
    [Fact(Timeout = 5000)]
    public void CacheValidation_should_preserve_uri_and_method_when_building_conditional_request()
    {
        var entry = MakeEntry(etag: "\"v1\"");
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page");

        var conditional = CacheValidationRequestBuilder.BuildConditionalRequest(original, entry);

        Assert.Equal(original.RequestUri, conditional.RequestUri);
        Assert.Equal(HttpMethod.Get, conditional.Method);
    }


    [Trait("RFC", "RFC9111-4.3.2")]
    [Fact(Timeout = 5000)]
    public void CacheValidation_should_return_false_when_no_validators_present()
    {
        var entry = MakeEntry();
        Assert.False(CacheValidationRequestBuilder.CanRevalidate(entry));
    }

    [Trait("RFC", "RFC9111-4.3.2")]
    [Fact(Timeout = 5000)]
    public void CacheValidation_should_return_true_when_etag_present()
    {
        var entry = MakeEntry(etag: "\"v1\"");
        Assert.True(CacheValidationRequestBuilder.CanRevalidate(entry));
    }

    [Trait("RFC", "RFC9111-4.3.2")]
    [Fact(Timeout = 5000)]
    public void CacheValidation_should_return_true_when_last_modified_present()
    {
        var entry = MakeEntry(lastModified: _baseTime);
        Assert.True(CacheValidationRequestBuilder.CanRevalidate(entry));
    }


    [Trait("RFC", "RFC9111-4.3.4")]
    [Fact(Timeout = 5000)]
    public void CacheValidation_should_return_200_status_code_when_merging_not_modified_response()
    {
        var entry = MakeEntry(etag: "\"v1\"");
        var notModified = new HttpResponseMessage(HttpStatusCode.NotModified);

        var merged = CacheValidationRequestBuilder.MergeNotModifiedResponse(notModified, entry);

        Assert.Equal(HttpStatusCode.OK, merged.StatusCode);
    }

    [Trait("RFC", "RFC9111-4.3.4")]
    [Fact(Timeout = 5000)]
    public async Task CacheValidation_should_return_cached_body_when_merging_not_modified_response()
    {
        var entry = MakeEntry(etag: "\"v1\"");
        var notModified = new HttpResponseMessage(HttpStatusCode.NotModified);

        var merged = CacheValidationRequestBuilder.MergeNotModifiedResponse(notModified, entry);

        var body = await merged.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(entry.Body, body);
    }

    [Trait("RFC", "RFC9111-4.3.4")]
    [Fact(Timeout = 5000)]
    public void CacheValidation_should_override_cached_etag_when_304_has_new_etag_header()
    {
        var entry = MakeEntry(etag: "\"old\"");
        var notModified = new HttpResponseMessage(HttpStatusCode.NotModified);
        notModified.Headers.TryAddWithoutValidation("ETag", "\"new\"");

        var merged = CacheValidationRequestBuilder.MergeNotModifiedResponse(notModified, entry);

        var etag = string.Join("", merged.Headers.GetValues("ETag"));
        Assert.Equal("\"new\"", etag);
    }

    [Trait("RFC", "RFC9111-4.3.4")]
    [Fact(Timeout = 5000)]
    public void CacheValidation_should_preserve_version_when_merging_not_modified_response()
    {
        var entry = MakeEntry();
        entry.Response.Version = new Version(2, 0);
        var notModified = new HttpResponseMessage(HttpStatusCode.NotModified);

        var merged = CacheValidationRequestBuilder.MergeNotModifiedResponse(notModified, entry);

        Assert.Equal(new Version(2, 0), merged.Version);
    }


    [Trait("RFC", "RFC9111-4.3.5")]
    [Fact(Timeout = 5000)]
    public void CacheValidation_should_build_head_request_when_stale_entry()
    {
        var entry = MakeEntry(etag: "\"v1\"", lastModified: _baseTime);
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");

        var head = CacheValidationRequestBuilder.BuildHeadValidationRequest(original, entry);

        Assert.Equal(HttpMethod.Head, head.Method);
        Assert.Equal(original.RequestUri, head.RequestUri);
        Assert.True(head.Headers.Contains("If-None-Match"));
        var etag = string.Join("", head.Headers.GetValues("If-None-Match"));
        Assert.Equal("\"v1\"", etag);
        Assert.NotNull(head.Headers.IfModifiedSince);
        Assert.Equal(_baseTime, head.Headers.IfModifiedSince!.Value);
    }

    [Trait("RFC", "RFC9111-4.3.5")]
    [Fact(Timeout = 5000)]
    public void CacheValidation_should_freshen_when_head_304_with_matching_etag()
    {
        var entry = MakeEntry(etag: "\"v1\"");
        var head304 = new HttpResponseMessage(HttpStatusCode.NotModified);
        head304.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"v1\"");
        head304.Headers.TryAddWithoutValidation("X-Cache-Refreshed", "true");

        var freshened = CacheValidationRequestBuilder.TryFreshenFromHead(head304, entry);

        Assert.True(freshened);
        Assert.True(entry.Response.Headers.Contains("X-Cache-Refreshed"));
    }

    [Trait("RFC", "RFC9111-4.3.5")]
    [Fact(Timeout = 5000)]
    public void CacheValidation_should_not_freshen_when_etag_mismatch()
    {
        var entry = MakeEntry(etag: "\"v1\"");
        var head304 = new HttpResponseMessage(HttpStatusCode.NotModified);
        head304.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"v2\"");

        var freshened = CacheValidationRequestBuilder.TryFreshenFromHead(head304, entry);

        Assert.False(freshened);
    }
}
