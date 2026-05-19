using System.Net;
using TurboHTTP.Features.Cookies;
using TurboHTTP.Internal;

namespace TurboHTTP.Protocol.Semantics;

/// <summary>
/// RFC 9110 §15.4 — Redirect handling for HTTP clients.
/// Implements correct semantics for 301, 302, 303, 307, and 308 status codes
/// including method rewriting, body preservation, loop detection,
/// max-redirect enforcement, and cross-origin security rules.
/// </summary>
internal sealed class RedirectHandler
{
    private readonly RedirectPolicy _policy;
    private readonly HashSet<string> _visitedUris;

    /// <summary>
    /// Creates a new redirect handler with the specified policy.
    /// </summary>
    /// <param name="policy">Redirect policy configuration. Defaults to <see cref="RedirectPolicy.Default"/>.</param>
    public RedirectHandler(RedirectPolicy? policy = null)
    {
        _policy = policy ?? RedirectPolicy.Default;
        _visitedUris = new HashSet<string>(StringComparer.Ordinal);
        RedirectCount = 0;
    }

    /// <summary>
    /// Returns true if the response status code is a redirect that should be followed.
    /// </summary>
    public static bool IsRedirect(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return response.StatusCode is HttpStatusCode.MovedPermanently // 301
            or HttpStatusCode.Found // 302
            or HttpStatusCode.SeeOther // 303
            or HttpStatusCode.TemporaryRedirect // 307
            or HttpStatusCode.PermanentRedirect; // 308
    }

    /// <summary>
    /// Builds a new <see cref="HttpRequestMessage"/> for the redirect location,
    /// applying RFC 9110 §15.4 semantics for method rewriting, body preservation,
    /// and security-sensitive header stripping.
    /// </summary>
    /// <param name="original">The original request that triggered the redirect.</param>
    /// <param name="response">The redirect response received.</param>
    /// <returns>A new request targeting the redirect location.</returns>
    /// <exception cref="RedirectException">
    /// Thrown when the max redirect limit is exceeded, a redirect loop is detected,
    /// or the Location header is missing/invalid.
    /// </exception>
    /// <exception cref="RedirectException">
    /// Also thrown with <see cref="RedirectError.ProtocolDowngrade"/> when the redirect
    /// would downgrade from HTTPS to HTTP and
    /// <see cref="RedirectPolicy.AllowHttpsToHttpDowngrade"/> is false.
    /// </exception>
    public HttpRequestMessage BuildRedirectRequest(HttpRequestMessage original, HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(original.RequestUri);

        // Register the current URL on first call (before first redirect)
        if (RedirectCount == 0)
        {
            var normalized = NormalizeUriForComparison(original.RequestUri);
            System.Diagnostics.Debug.WriteLine($"[Redirect] Initial URI: {original.RequestUri} → normalized: {normalized}");
            _visitedUris.Add(normalized);
        }

        // Enforce max redirects
        if (RedirectCount >= _policy.MaxRedirects)
        {
            throw new RedirectException(
                $"RFC 9110 §15.4: Maximum redirect limit of {_policy.MaxRedirects} exceeded.",
                RedirectError.MaxRedirectsExceeded);
        }

        var locationUri = ResolveLocationUri(original.RequestUri, response);
        System.Diagnostics.Debug.WriteLine($"[Redirect] Redirect #{RedirectCount + 1}: LocationUri={locationUri}");

        // Detect HTTPS → HTTP downgrade
        if (!_policy.AllowHttpsToHttpDowngrade &&
            original.RequestUri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) &&
            locationUri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase))
        {
            throw new RedirectException(
                $"RFC 9110 §15.4: Redirect from HTTPS to HTTP is not allowed (downgrade detected). " +
                $"Redirect location: {locationUri}",
                RedirectError.ProtocolDowngrade);
        }

        // Detect redirect loops — normalized comparison is case-insensitive for
        // scheme/host and case-sensitive for path/query; fragments are ignored.
        var normalizedLocation = NormalizeUriForComparison(locationUri);
        System.Diagnostics.Debug.WriteLine($"[Redirect] Normalized location: {normalizedLocation}, visited count: {_visitedUris.Count}, visited: {string.Join(", ", _visitedUris)}");
        if (!_visitedUris.Add(normalizedLocation))
        {
            throw new RedirectException(
                $"RFC 9110 §15.4: Redirect loop detected. URI already visited: {normalizedLocation}",
                RedirectError.RedirectLoop);
        }

        RedirectCount++;

        // Determine new method and whether to preserve the body
        var (newMethod, preserveBody) = ResolveMethodAndBody(original.Method, response.StatusCode);

        // Build the new request — preserve Version from original (e.g. HTTP/2 stays HTTP/2)
        var newRequest = new HttpRequestMessage(newMethod, locationUri)
        {
            Version = original.Version
        };

        // Copy request options (e.g. RequestId) so response correlation is preserved
        CopyOptions(original, newRequest);

        // Copy non-sensitive headers from the original request
        var isCrossOrigin = IsCrossOrigin(original.RequestUri, locationUri);
        CopyHeaders(original, newRequest, isCrossOrigin);

        // Preserve body if applicable — buffer content bytes because the encoder
        // disposes the stream on first use, breaking re-reads on redirect.
        if (preserveBody && original.Content != null)
        {
            var ms = RecyclableStreams.Manager.GetStream();
            original.Content.CopyTo(ms, null, CancellationToken.None);
            ms.Position = 0;
            var newContent = new StreamContent(ms);
            foreach (var h in original.Content.Headers)
            {
                newContent.Headers.TryAddWithoutValidation(h.Key, h.Value);
            }

            newRequest.Content = newContent;
        }

        return newRequest;
    }

    /// <summary>
    /// Resets the redirect state, allowing the handler to be reused for a new request chain.
    /// </summary>
    public void Reset()
    {
        _visitedUris.Clear();
        RedirectCount = 0;
    }

    /// <summary>Gets the current redirect count for the active chain.</summary>
    public int RedirectCount { get; private set; }

    /// <summary>
    /// Normalizes a URI for redirect loop comparison.
    /// Scheme and host are lowered (case-insensitive), path and query are preserved
    /// (case-sensitive), and fragment is discarded (not sent to server).
    /// </summary>
    private static string NormalizeUriForComparison(Uri uri)
    {
        var port = uri.IsDefaultPort ? "" : $":{uri.Port}";
        return $"{uri.Scheme.ToLowerInvariant()}://{uri.Host.ToLowerInvariant()}{port}{uri.AbsolutePath}{uri.Query}";
    }

    private static Uri ResolveLocationUri(Uri baseUri, HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues(WellKnownHeaders.Location, out var locationValues))
        {
            throw new RedirectException(
                "RFC 9110 §15.4: Redirect response is missing the Location header.",
                RedirectError.MissingLocationHeader);
        }

        var locationValue = string.Empty;
        foreach (var v in locationValues)
        {
            locationValue = v;
            break;
        }

        if (string.IsNullOrWhiteSpace(locationValue))
        {
            throw new RedirectException("RFC 9110 §15.4: Location header is empty.",
                RedirectError.MissingLocationHeader);
        }

        // Handle "http:///path" or "https:///path" — empty authority.
        // .NET Uri rejects these, but HTTP/1.0 backends that lack a Host header
        // may produce them. Strip scheme+empty-authority and resolve as relative.
        if (locationValue.StartsWith("http:///", StringComparison.OrdinalIgnoreCase))
        {
            locationValue = locationValue[("http://".Length)..];
        }
        else if (locationValue.StartsWith("https:///", StringComparison.OrdinalIgnoreCase))
        {
            locationValue = locationValue[("https://".Length)..];
        }

        // Resolve relative URIs against the request URI (RFC 9110 §10.2.2).
        // On Linux, Uri.TryCreate treats paths starting with "/" as absolute file:// URIs,
        // so we must verify that the result has an http/https scheme.
        if (Uri.TryCreate(locationValue, UriKind.Absolute, out var absoluteUri) &&
            (absoluteUri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) ||
             absoluteUri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)))
        {
            return absoluteUri;
        }

        if (Uri.TryCreate(baseUri, locationValue, out var resolvedUri))
        {
            return resolvedUri;
        }

        throw new RedirectException(
            $"RFC 9110 §15.4: Location header value '{locationValue}' is not a valid URI.",
            RedirectError.InvalidLocationHeader);
    }

    private static (HttpMethod Method, bool PreserveBody) ResolveMethodAndBody(
        HttpMethod originalMethod,
        HttpStatusCode statusCode)
    {
        return statusCode switch
        {
            // 303 See Other: ALWAYS rewrite to GET, never preserve body (RFC 9110 §15.4.4)
            HttpStatusCode.SeeOther => (HttpMethod.Get, false),

            // 307 Temporary Redirect: MUST preserve method and body (RFC 9110 §15.4.8)
            HttpStatusCode.TemporaryRedirect => (originalMethod, true),

            // 308 Permanent Redirect: MUST preserve method and body (RFC 9110 §15.4.9)
            HttpStatusCode.PermanentRedirect => (originalMethod, true),

            // 301 Moved Permanently: historical practice — rewrite POST to GET (RFC 9110 §15.4.2)
            // 302 Found: historical practice — rewrite POST to GET (RFC 9110 §15.4.3)
            HttpStatusCode.MovedPermanently or HttpStatusCode.Found =>
                originalMethod == HttpMethod.Post
                    ? (HttpMethod.Get, false)
                    : (originalMethod, false),

            _ => (originalMethod, false)
        };
    }

    private static bool IsCrossOrigin(Uri original, Uri redirect)
    {
        return !string.Equals(original.Scheme, redirect.Scheme, StringComparison.OrdinalIgnoreCase) ||
               !string.Equals(original.Host, redirect.Host, StringComparison.OrdinalIgnoreCase) ||
               original.Port != redirect.Port;
    }

    /// <summary>
    /// Builds a new <see cref="HttpRequestMessage"/> for the redirect location,
    /// applying RFC 9110 §15.4 semantics, and re-evaluates cookies for the new URI
    /// using the provided <paramref name="cookieJar"/>.
    ///
    /// Cookies are never blindly forwarded on redirect. The jar first processes any
    /// Set-Cookie headers from the redirect response, then re-applies applicable
    /// cookies to the new request based on domain, path, Secure, and expiry rules.
    /// </summary>
    /// <param name="original">The original request that triggered the redirect.</param>
    /// <param name="response">The redirect response received.</param>
    /// <param name="cookieJar">The cookie jar to use for re-evaluation.</param>
    public HttpRequestMessage BuildRedirectRequest(HttpRequestMessage original, HttpResponseMessage response,
        CookieJar cookieJar)
    {
        ArgumentNullException.ThrowIfNull(cookieJar);
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(original.RequestUri);

        // Process Set-Cookie headers from the redirect response into the jar
        cookieJar.ProcessResponse(original.RequestUri, response);

        // Build the redirect request (Cookie header is stripped by CopyHeaders)
        var newRequest = BuildRedirectRequest(original, response);

        // Re-apply cookies for the new redirect URI from the jar
        if (newRequest.RequestUri is not null)
        {
            cookieJar.AddCookiesToRequest(newRequest.RequestUri, ref newRequest);
        }

        return newRequest;
    }

    private static void CopyOptions(HttpRequestMessage original, HttpRequestMessage newRequest)
    {
        foreach (var kvp in original.Options)
        {
            ((IDictionary<string, object?>)newRequest.Options)[kvp.Key] = kvp.Value;
        }
    }

    private static void CopyHeaders(
        HttpRequestMessage original,
        HttpRequestMessage newRequest,
        bool isCrossOrigin)
    {
        foreach (var header in original.Headers)
        {
            // RFC 9110 §15.4: Do NOT forward Authorization header across origins
            if (isCrossOrigin &&
                header.Key.Equals(WellKnownHeaders.Authorization, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Do not copy Host — it will be set based on the new URI
            if (header.Key.Equals(WellKnownHeaders.Host, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // RFC 6265 §5.4: Do NOT blindly forward Cookie header on redirect.
            // Cookies must be re-evaluated per redirect URI (domain, path, Secure, expiry).
            // Use BuildRedirectRequest(original, response, cookieJar) to re-apply cookies.
            if (header.Key.Equals(WellKnownHeaders.Cookie, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            newRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
    }
}