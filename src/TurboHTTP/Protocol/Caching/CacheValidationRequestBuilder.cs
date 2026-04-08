using System.Net;

namespace TurboHTTP.Protocol.Caching;

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
    public static HttpRequestMessage BuildConditionalRequest(HttpRequestMessage original, CacheEntry entry)
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

        // Copy Options so that request correlation (requestId) survives the conditional request
#if NET5_0_OR_GREATER
        foreach (var option in original.Options)
        {
            conditional.Options.Set(new HttpRequestOptionsKey<object?>(option.Key), option.Value);
        }
#endif

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
    public static HttpResponseMessage MergeNotModifiedResponse(HttpResponseMessage notModifiedResponse,
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
    public static bool CanRevalidate(CacheEntry entry) => entry.ETag is not null || entry.LastModified.HasValue;

    /// <summary>
    /// RFC 9111 §4.3.5 — Builds a HEAD validation request for a stale cache entry.
    /// Uses HEAD instead of GET so the origin server can confirm freshness without
    /// transmitting the full body. Adds If-None-Match and/or If-Modified-Since headers
    /// from the cached entry's validators.
    /// </summary>
    public static HttpRequestMessage BuildHeadValidationRequest(HttpRequestMessage original, CacheEntry entry)
    {
        var head = new HttpRequestMessage(HttpMethod.Head, original.RequestUri)
        {
            Version = original.Version
        };

        // Copy original request headers (except content-related ones, HEAD has no body)
        foreach (var header in original.Headers)
        {
            head.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        // RFC 9111 §4.3.1 — If-None-Match from ETag
        if (entry.ETag is not null)
        {
            head.Headers.TryAddWithoutValidation("If-None-Match", entry.ETag);
        }

        // RFC 9111 §4.3.1 — If-Modified-Since from Last-Modified
        if (entry.LastModified.HasValue)
        {
            head.Headers.IfModifiedSince = entry.LastModified;
        }

        return head;
    }

    /// <summary>
    /// RFC 9111 §4.3.5 — Attempts to freshen a stored GET response using a HEAD 304 response.
    /// Returns true and updates the cache entry's response headers if the HEAD response's ETag
    /// matches the stored entry's ETag. Returns false when the ETag does not match, meaning
    /// the stored response is stale and must not be freshened.
    /// </summary>
    public static bool TryFreshenFromHead(HttpResponseMessage headResponse, CacheEntry entry)
    {
        if (headResponse.StatusCode != HttpStatusCode.NotModified)
        {
            return false;
        }

        // Compare ETags — the HEAD 304 must carry an ETag that matches the stored entry
        var headETag = headResponse.Headers.ETag?.ToString();
        if (headETag is null || entry.ETag is null || !string.Equals(headETag, entry.ETag, StringComparison.Ordinal))
        {
            return false;
        }

        // Freshen: update stored response headers from the 304 response
        foreach (var header in headResponse.Headers)
        {
            // Skip ETag — already validated
            if (string.Equals(header.Key, "ETag", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            entry.Response.Headers.Remove(header.Key);
            entry.Response.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return true;
    }
}
