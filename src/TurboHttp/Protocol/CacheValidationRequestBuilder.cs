using System.Net;
using System.Net.Http;

namespace TurboHttp.Protocol;

/// <summary>
/// RFC 9111 §4.3 — Builds conditional revalidation requests and merges 304 responses.
/// </summary>
public static class CacheValidationRequestBuilder
{
    /// <summary>
    /// RFC 9111 §4.3.1 — Creates a conditional request from the original request by adding
    /// If-None-Match (from ETag) and/or If-Modified-Since (from Last-Modified) headers.
    /// The returned request shares the same URI, method, version, and content as the original.
    /// </summary>
    public static HttpRequestMessage BuildConditionalRequest(
        HttpRequestMessage original,
        CacheEntry entry)
    {
        var conditional = new HttpRequestMessage(original.Method, original.RequestUri)
        {
            Version = original.Version,
            Content = original.Content
        };

        // Copy original request headers
        foreach (var header in original.Headers)
        {
            conditional.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        // RFC 9111 §4.3.1 — If-None-Match from ETag (preferred over If-Modified-Since)
        if (entry.ETag is not null)
        {
            conditional.Headers.TryAddWithoutValidation("If-None-Match", entry.ETag);
        }

        // RFC 9111 §4.3.1 — If-Modified-Since from Last-Modified
        if (entry.LastModified.HasValue)
        {
            conditional.Headers.IfModifiedSince = entry.LastModified;
        }

        return conditional;
    }

    /// <summary>
    /// RFC 9111 §4.3.4 — Merges headers from a 304 Not Modified response with the cached entry.
    /// Returns a new 200 OK response with the cached body and the merged headers.
    /// Headers present in the 304 response override those in the cached entry.
    /// </summary>
    public static HttpResponseMessage MergeNotModifiedResponse(
        HttpResponseMessage notModifiedResponse,
        CacheEntry cachedEntry)
    {
        // RFC 9111 §4.3.4: construct a new 200 response using stored headers + body
        var merged = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Version = cachedEntry.Response.Version,
            Content = new ByteArrayContent(cachedEntry.Body)
        };

        // Copy cached response headers as baseline
        foreach (var header in cachedEntry.Response.Headers)
        {
            merged.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        // Copy cached content headers as baseline
        foreach (var header in cachedEntry.Response.Content.Headers)
        {
            merged.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        // RFC 9111 §4.3.4 — headers from 304 override cached headers
        foreach (var header in notModifiedResponse.Headers)
        {
            merged.Headers.Remove(header.Key);
            merged.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return merged;
    }

    /// <summary>
    /// RFC 9111 §4.3.2 — Returns true if the cache entry has an ETag or a Last-Modified date,
    /// which means a conditional request can be built.
    /// </summary>
    public static bool CanRevalidate(CacheEntry entry)
        => entry.ETag is not null || entry.LastModified.HasValue;
}
