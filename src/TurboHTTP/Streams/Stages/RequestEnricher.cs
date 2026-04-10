using System.Net;
using TurboHTTP.Protocol.Semantics;

namespace TurboHTTP.Streams.Stages;

/// <summary>
/// Stateless request enrichment logic extracted from the former <see cref="RequestEnricher"/>.
/// Applied as a <c>Select()</c> transform in the pipeline — no separate GraphStage needed.
/// Handles: URI resolution, version defaults, header merging, Referer sanitization, If-Range validation.
/// </summary>
internal sealed class RequestEnricher
{
    private readonly Func<TurboRequestOptions> _optionsFactory;

    public RequestEnricher(Func<TurboRequestOptions> optionsFactory)
    {
        _optionsFactory = optionsFactory;
    }

    public HttpRequestMessage Enrich(HttpRequestMessage request)
    {
        var options = _optionsFactory.Invoke();

        // Rule 1: URI resolution
        if (request.RequestUri is null || !request.RequestUri.IsAbsoluteUri)
        {
            var baseAddress = options.BaseAddress;
            if (baseAddress is null)
            {
                throw new InvalidOperationException(
                    "RequestUri is null or relative but no BaseAddress is configured.");
            }

            request.RequestUri = request.RequestUri is null
                ? baseAddress
                : new Uri(baseAddress, request.RequestUri);
        }

        // Rule 2: Version — only override when request is still at the 1.1 default
        if (request.Version == HttpVersion.Version11 && options.DefaultRequestVersion != HttpVersion.Version11)
        {
            request.Version = options.DefaultRequestVersion;
        }

        // Rule 3: Default headers — add those absent from the request
        foreach (var header in options.DefaultRequestHeaders)
        {
            if (!request.Headers.Contains(header.Key))
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        // Rule 4 removed: RFC 9110 §6.6.1 — clients SHOULD NOT send Date.

        // Rule 5: Referer sanitization (RFC 9110 §10.5)
        SanitizeReferer(request);

        // Rule 6: If-Range validation (RFC 9110 §13.1.5)
        IfRangeValidator.Validate(request);

        return request;
    }

    /// <summary>
    /// RFC 9110 §10.5:
    /// - Strip fragment and userinfo from Referer URI
    /// - Remove Referer on HTTPS→HTTP downgrade
    /// </summary>
    private static void SanitizeReferer(HttpRequestMessage request)
    {
        if (!request.Headers.TryGetValues("Referer", out var values))
        {
            return;
        }

        var refererValue = values.FirstOrDefault();
        if (string.IsNullOrEmpty(refererValue) || !Uri.TryCreate(refererValue, UriKind.Absolute, out var refererUri))
        {
            return;
        }

        // HTTPS→HTTP downgrade: remove Referer entirely
        if (refererUri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)
            && request.RequestUri is not null
            && request.RequestUri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase))
        {
            request.Headers.Remove("Referer");
            return;
        }

        // Strip fragment and userinfo
        var needsStrip = !string.IsNullOrEmpty(refererUri.Fragment)
                         || !string.IsNullOrEmpty(refererUri.UserInfo);

        if (!needsStrip) return;
        var sanitized = UriSanitizer.FormatAbsoluteWithoutUserInfo(refererUri);
        request.Headers.Remove("Referer");
        request.Headers.TryAddWithoutValidation("Referer", sanitized);
    }
}