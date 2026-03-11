using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;

namespace TurboHttp.Protocol;

/// <summary>
/// RFC 9110 §15.4 — Redirect handling for HTTP clients.
/// Implements correct semantics for 301, 302, 303, 307, and 308 status codes
/// including method rewriting, body preservation, loop detection,
/// max-redirect enforcement, and cross-origin security rules.
/// </summary>
public sealed class RedirectHandler
{
    private readonly RedirectPolicy _policy;
    private readonly HashSet<string> _visitedUris;
    private int _redirectCount;

    /// <summary>
    /// Creates a new redirect handler with the specified policy.
    /// </summary>
    /// <param name="policy">Redirect policy configuration. Defaults to <see cref="RedirectPolicy.Default"/>.</param>
    public RedirectHandler(RedirectPolicy? policy = null)
    {
        _policy = policy ?? RedirectPolicy.Default;
        _visitedUris = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _redirectCount = 0;
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
    /// <exception cref="RedirectDowngradeException">
    /// Thrown when the redirect would downgrade from HTTPS to HTTP and
    /// <see cref="RedirectPolicy.AllowHttpsToHttpDowngrade"/> is false.
    /// </exception>
    public HttpRequestMessage BuildRedirectRequest(HttpRequestMessage original, HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(original.RequestUri);

        // Register the current URL on first call (before first redirect)
        if (_redirectCount == 0)
        {
            _visitedUris.Add(original.RequestUri.AbsoluteUri);
        }

        // Enforce max redirects
        if (_redirectCount >= _policy.MaxRedirects)
        {
            throw new RedirectException(
                $"RFC 9110 §15.4: Maximum redirect limit of {_policy.MaxRedirects} exceeded.",
                RedirectError.MaxRedirectsExceeded);
        }

        // Extract and validate Location header
        var locationUri = ResolveLocationUri(original.RequestUri, response);

        // Detect HTTPS → HTTP downgrade
        if (!_policy.AllowHttpsToHttpDowngrade &&
            original.RequestUri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) &&
            locationUri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase))
        {
            throw new RedirectDowngradeException(
                $"RFC 9110 §15.4: Redirect from HTTPS to HTTP is not allowed (downgrade detected). " +
                $"Redirect location: {locationUri}");
        }

        // Detect redirect loops
        var locationStr = locationUri.AbsoluteUri;
        if (!_visitedUris.Add(locationStr))
        {
            throw new RedirectException(
                $"RFC 9110 §15.4: Redirect loop detected. URI already visited: {locationStr}",
                RedirectError.RedirectLoop);
        }

        _redirectCount++;

        // Determine new method and whether to preserve the body
        var (newMethod, preserveBody) = ResolveMethodAndBody(original.Method, response.StatusCode);

        // Build the new request
        var newRequest = new HttpRequestMessage(newMethod, locationUri);

        // Copy non-sensitive headers from the original request
        var isCrossOrigin = IsCrossOrigin(original.RequestUri, locationUri);
        CopyHeaders(original, newRequest, isCrossOrigin);

        // Preserve body if applicable
        if (preserveBody && original.Content != null)
        {
            newRequest.Content = original.Content;
        }

        return newRequest;
    }

    /// <summary>
    /// Resets the redirect state, allowing the handler to be reused for a new request chain.
    /// </summary>
    public void Reset()
    {
        _visitedUris.Clear();
        _redirectCount = 0;
    }

    /// <summary>Gets the current redirect count for the active chain.</summary>
    public int RedirectCount => _redirectCount;

    // ── Private Helpers ──────────────────────────────────────────────────────────

    private static Uri ResolveLocationUri(Uri baseUri, HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Location", out var locationValues))
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

        // Resolve relative URIs against the request URI (RFC 9110 §10.2.2)
        if (Uri.TryCreate(locationValue, UriKind.Absolute, out var absoluteUri))
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

    private static void CopyHeaders(
        HttpRequestMessage original,
        HttpRequestMessage newRequest,
        bool isCrossOrigin)
    {
        foreach (var header in original.Headers)
        {
            // RFC 9110 §15.4: Do NOT forward Authorization header across origins
            if (isCrossOrigin &&
                header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Do not copy Host — it will be set based on the new URI
            if (header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // RFC 6265 §5.4: Do NOT blindly forward Cookie header on redirect.
            // Cookies must be re-evaluated per redirect URI (domain, path, Secure, expiry).
            // Use BuildRedirectRequest(original, response, cookieJar) to re-apply cookies.
            if (header.Key.Equals("Cookie", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            newRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
    }
}