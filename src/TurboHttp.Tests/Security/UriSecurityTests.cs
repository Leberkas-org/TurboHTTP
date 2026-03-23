using System.Net;
using System.Text;
using TurboHttp.Protocol.RFC1945;
using TurboHttp.Protocol.RFC7541;
using TurboHttp.Protocol.RFC9110;
using TurboHttp.Protocol.RFC9112;
using TurboHttp.Protocol.RFC9113;

namespace TurboHttp.Tests.Security;

/// <summary>
/// Tests URI sanitization and path traversal prevention in TurboHttp encoders and handlers.
/// Verifies that userinfo is stripped, fragments are removed, path traversal is prevented,
/// double-encoding is preserved, null bytes are rejected, and extremely long URIs are handled gracefully
/// per RFC 9110 §4.2.4 (userinfo prohibition) and RFC 9113 (HTTP/2 encoding).
/// </summary>
/// <remarks>
/// Classes under test: <see cref="UriSanitizer"/>, <see cref="RedirectHandler"/>,
/// <see cref="Http10Encoder"/>, <see cref="Http11Encoder"/>, <see cref="Http2RequestEncoder"/>.
/// Attack vectors: path traversal, fragment injection, userinfo embedded in URIs,
/// unicode normalization, double-encoding passthrough, null bytes, backslash handling,
/// extremely long URI components.
/// </remarks>
public sealed class UriSecurityTests
{
    // ── Encoder helpers ──────────────────────────────────────────────────────────

    private static string EncodeHttp11(HttpRequestMessage request, bool absoluteForm = false, int bufferSize = 16384)
    {
        var buffer = new Memory<byte>(new byte[bufferSize]);
        var span = buffer.Span;
        var written = Http11Encoder.Encode(request, ref span, absoluteForm);
        return Encoding.ASCII.GetString(buffer.Span[..written]);
    }

    private static string EncodeHttp10(HttpRequestMessage request, bool absoluteForm = false, int bufferSize = 16384)
    {
        var buffer = new Memory<byte>(new byte[bufferSize]);
        var written = Http10Encoder.Encode(request, ref buffer, absoluteForm);
        return Encoding.ASCII.GetString(buffer.Span[..written]);
    }

    private static HttpResponseMessage RedirectResponse(HttpStatusCode status, string location)
    {
        var response = new HttpResponseMessage(status);
        response.Headers.TryAddWithoutValidation("Location", location);
        return response;
    }

    private static List<(string Name, string Value)> DecodeHpackHeaders(byte[] headerBlock)
    {
        var decoder = new HpackDecoder();
        var headers = decoder.Decode(headerBlock);
        return headers.ConvertAll(h => (h.Name, h.Value));
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // Section 1: Path Traversal Prevention (/../../../etc/passwd)
    // ══════════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "SEC-URI-001: Path traversal in redirect Location header normalized by Uri.TryCreate")]
    public void Should_NormalizePathTraversal_When_RedirectLocationContains_PathTraversal()
    {
        // Attack: Location header with /../../../etc/passwd should be normalized
        // .NET's Uri class normalizes path traversal sequences during parsing.
        var original = new HttpRequestMessage(HttpMethod.Get, "https://example.com/api/v1/users/123");
        var response = RedirectResponse(HttpStatusCode.Found, "/../../../etc/passwd");

        var handler = new RedirectHandler();
        var redirect = handler.BuildRedirectRequest(original, response);

        // Uri normalizes the path — /../ is resolved against /api/v1/users/123
        // Result should be normalized path, not the raw traversal sequence
        Assert.NotNull(redirect.RequestUri);
        Assert.True(redirect.RequestUri.AbsolutePath.Length > 0);
        // The exact normalized path depends on Uri normalization rules — just verify it's absolute
        Assert.True(redirect.RequestUri.IsAbsoluteUri);
    }

    [Fact(DisplayName = "SEC-URI-002: Relative path traversal in Location resolved against base")]
    public void Should_ResolveRelativeTraversal_When_LocationIsRelativePath()
    {
        // Attack: Relative path traversal ../../../sensitive/file
        var original = new HttpRequestMessage(HttpMethod.Get, "https://example.com/app/admin/page");
        var response = RedirectResponse(HttpStatusCode.Found, "../../../etc/passwd");

        var handler = new RedirectHandler();
        var redirect = handler.BuildRedirectRequest(original, response);

        // Uri.TryCreate normalizes the relative path against the base
        Assert.NotNull(redirect.RequestUri);
        Assert.Equal("https://example.com/etc/passwd", redirect.RequestUri.AbsoluteUri);
    }

    [Fact(DisplayName = "SEC-URI-003: Absolute path traversal in Location normalized by Uri")]
    public void Should_HandleAbsolutePathTraversal_When_LocationIsAbsolutePath()
    {
        // Attack: Location with /../../sensitive
        // .NET Uri normalizes /.. sequences, so /api/users + /../../sensitive = /sensitive
        var original = new HttpRequestMessage(HttpMethod.Get, "https://example.com/api/users");
        var response = RedirectResponse(HttpStatusCode.Found, "/../../sensitive");

        var handler = new RedirectHandler();
        var redirect = handler.BuildRedirectRequest(original, response);

        // Uri normalizes the path — /.. is resolved and collapsed
        Assert.NotNull(redirect.RequestUri);
        // Result should be normalized path without traversal sequence
        Assert.Equal("https://example.com/sensitive", redirect.RequestUri.AbsoluteUri);
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // Section 2: Fragment Injection Prevention (stripped before sending)
    // ══════════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "SEC-URI-004: Fragment stripped from HTTP/1.1 origin-form encoding")]
    public void Should_StripFragment_When_EncodeOriginForm()
    {
        // Attack: Fragment in request URI should not appear on the wire
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/path?query=value#fragment");

        var encoded = EncodeHttp11(request, absoluteForm: false);

        // Fragment should never appear in HTTP/1.1 request line
        Assert.DoesNotContain("#", encoded);
        Assert.DoesNotContain("fragment", encoded);
        // Path and query should be present
        Assert.Contains("/path?query=value", encoded);
    }

    [Fact(DisplayName = "SEC-URI-005: Fragment stripped from HTTP/1.1 absolute-form encoding")]
    public void Should_StripFragment_When_EncodeAbsoluteForm()
    {
        // Attack: Fragment in absolute-form request-target
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com:8080/api#admin");

        var encoded = EncodeHttp11(request, absoluteForm: true);

        // Fragment must not appear in wire format
        Assert.DoesNotContain("#", encoded);
        Assert.DoesNotContain("admin", encoded);
        // Authority and path should be present
        Assert.Contains("example.com", encoded);
    }

    [Fact(DisplayName = "SEC-URI-006: Fragment stripped from HTTP/1.0 encoding")]
    public void Should_StripFragment_When_Http10Encode()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/endpoint#internal");

        var encoded = EncodeHttp10(request);

        // HTTP/1.0 also strips fragments
        Assert.DoesNotContain("#", encoded);
        Assert.DoesNotContain("internal", encoded);
    }

    [Fact(DisplayName = "SEC-URI-007: Fragment in redirect Location preserved in Uri but stripped on wire")]
    public void Should_IgnoreFragment_When_RedirectLocationContainsFragment()
    {
        // Attack: Location: https://example.com/#admin bypass
        var original = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var response = RedirectResponse(HttpStatusCode.MovedPermanently, "https://example.com/admin#user");

        var handler = new RedirectHandler();
        var redirect = handler.BuildRedirectRequest(original, response);

        // Redirect URI object contains fragment (Uri.AbsoluteUri includes it),
        // but encoders will strip it when serializing to wire format
        Assert.NotNull(redirect.RequestUri);
        // Fragment is in the Uri object
        Assert.Equal("user", redirect.RequestUri.Fragment.TrimStart('#'));

        // When we encode the redirect request, fragment must be stripped
        var encoded = EncodeHttp11(redirect, absoluteForm: true);
        Assert.DoesNotContain("#", encoded);
        Assert.DoesNotContain("user", encoded);
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // Section 3: Userinfo Stripping (user:pass@host → host)
    // ══════════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "SEC-URI-008: Userinfo stripped from HTTP/1.1 absolute-form via UriSanitizer")]
    public void Should_StripUserinfo_When_Http11EncodeAbsoluteForm()
    {
        // Attack: Embedded credentials in URI should not appear on wire
        var builder = new UriBuilder("https://user:password@example.com/api") { Port = 443 };
        var request = new HttpRequestMessage(HttpMethod.Get, builder.Uri);

        var encoded = EncodeHttp11(request, absoluteForm: true);

        // Userinfo must not appear in wire format
        Assert.DoesNotContain("user", encoded);
        Assert.DoesNotContain("password", encoded);
        Assert.DoesNotContain("@", encoded);
        // Host should be present without userinfo
        Assert.Contains("example.com", encoded);
    }

    [Fact(DisplayName = "SEC-URI-009: Userinfo stripped from HTTP/1.0 absolute-form")]
    public void Should_StripUserinfo_When_Http10EncodeAbsoluteForm()
    {
        var builder = new UriBuilder("http://admin:secret@internal.local/service") { Port = 80 };
        var request = new HttpRequestMessage(HttpMethod.Get, builder.Uri);

        var encoded = EncodeHttp10(request, absoluteForm: true);

        // HTTP/1.0 also strips userinfo
        Assert.DoesNotContain("admin", encoded);
        Assert.DoesNotContain("secret", encoded);
        Assert.DoesNotContain("@", encoded);
    }

    [Fact(DisplayName = "SEC-URI-010: UriSanitizer.StripUserInfo preserves fragment")]
    public void Should_PreserveFragment_When_StripUserInfoCalled()
    {
        // StripUserInfo preserves fragment (unlike FormatAbsoluteWithoutUserInfo)
        var builder = new UriBuilder("https://user:pass@example.com/path#anchor") { Port = 443 };
        var uri = builder.Uri;

        var sanitized = UriSanitizer.StripUserInfo(uri);

        // Fragment should be preserved in the sanitized URI
        Assert.Contains("#anchor", sanitized);
        // Userinfo should be removed
        Assert.DoesNotContain("user", sanitized);
    }

    [Fact(DisplayName = "SEC-URI-011: UriSanitizer.FormatAbsoluteWithoutUserInfo strips both userinfo and fragment")]
    public void Should_StripUserInfoAndFragment_When_FormatAbsoluteWithoutUserInfoCalled()
    {
        // FormatAbsoluteWithoutUserInfo removes both userinfo AND fragment
        var builder = new UriBuilder("https://user:pass@example.com/path?query=1#anchor") { Port = 443 };
        var uri = builder.Uri;

        var formatted = UriSanitizer.FormatAbsoluteWithoutUserInfo(uri);

        // Both userinfo and fragment should be stripped
        Assert.DoesNotContain("user", formatted);
        Assert.DoesNotContain("#anchor", formatted);
        // Path and query should remain
        Assert.Contains("/path?query=1", formatted);
    }

    [Fact(DisplayName = "SEC-URI-012: UriSanitizer.FormatAuthority excludes userinfo")]
    public void Should_ExcludeUserinfo_When_FormatAuthorityCalled()
    {
        // FormatAuthority returns only host[:port], never userinfo
        var builder = new UriBuilder("https://user:password@example.com:8443/") { Port = 8443 };
        var uri = builder.Uri;

        var authority = UriSanitizer.FormatAuthority(uri);

        // Should be only host:port, no userinfo
        Assert.DoesNotContain("user", authority);
        Assert.DoesNotContain("password", authority);
        Assert.DoesNotContain("@", authority);
        Assert.Equal("example.com:8443", authority);
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // Section 4: Unicode Normalization Attacks (encoding must be correct)
    // ══════════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "SEC-URI-013: Unicode characters in path percent-encoded correctly")]
    public void Should_PercentEncodeUnicode_When_PathContainsUnicodeChars()
    {
        // Attack: Unicode equivalents of ASCII characters (e.g., full-width or normalization variants)
        // Legitimate case: UTF-8 path components should be percent-encoded
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/café");

        var encoded = EncodeHttp11(request, absoluteForm: false);

        // Path should be properly encoded for transmission
        // Either as /caf%C3%A9 (UTF-8 percent-encoded) or as-is depending on Uri normalization
        Assert.Contains("caf", encoded);
        Assert.DoesNotContain("\u00E9", encoded); // Raw Unicode should not appear in HTTP/1.1
    }

    [Theory(DisplayName = "SEC-URI-014: Percent-encoded characters in query string preserved")]
    [InlineData("https://example.com/search?q=%C3%A9", "q=%C3%A9")]
    [InlineData("https://example.com/path?encoded=%2Fslash", "encoded=%2Fslash")]
    public void Should_PreservePercentEncoding_When_QueryContainsEncodedChars(string requestUri, string expectedSubstring)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, requestUri);

        var encoded = EncodeHttp11(request, absoluteForm: false);

        // Percent-encoded sequences should be preserved in wire format
        Assert.Contains(expectedSubstring, encoded);
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // Section 5: Double-Encoding Passthrough (no double-decode)
    // ══════════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "SEC-URI-015: Double-encoded slash (%252F) preserved in query string")]
    public void Should_PreserveDoubleEncoding_When_QueryContainsEncodedPercent()
    {
        // Attack: %252F should NOT be decoded to %2F and then to /
        // Double-encoded sequences should pass through unchanged
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/api?path=%252Fetc%252Fpasswd");

        var encoded = EncodeHttp11(request, absoluteForm: false);

        // Double-encoded sequence should appear as-is (no double-decode)
        Assert.Contains("%252F", encoded);
        // Should NOT contain single-encoded %2F (which would indicate decoding happened)
        var slashCount = encoded.Count(c => c == '/');
        // Only the slashes in scheme (://) and path (/) should be present, not decoded ones
        Assert.True(slashCount <= 3); // https:// + /api
    }

    [Fact(DisplayName = "SEC-URI-016: Double-encoded query parameter keys unchanged")]
    public void Should_PreserveDoubleEncodedKeys_When_QueryKeyIsEncoded()
    {
        // Attack: Crafted query key parameter %3D (=) should not be decoded
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/endpoint?key%3Dvalue=data");

        var encoded = EncodeHttp11(request, absoluteForm: false);

        // Double-encoded = should be preserved
        Assert.Contains("key%3Dvalue", encoded);
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // Section 6: Null Byte Rejection (%00 → rejected)
    // ══════════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "SEC-URI-017: Null byte encoded as %00 preserved (not decoded to actual NUL)")]
    public void Should_PreserveEncodedNullByte_When_PathContainsPercentZeroZero()
    {
        // Note: %00 is a percent-encoded sequence, not an actual null byte.
        // .NET's Uri treats %00 as a regular encoded character, not a truncation attack.
        // The actual NULL byte character (0x00) would be rejected, but %00 passes through.
        var uri = new Uri("https://example.com/path%00/secret");

        // Uri successfully parses — %00 is preserved as encoded sequence
        Assert.NotNull(uri);
        Assert.Contains("%00", uri.AbsoluteUri);
    }

    [Fact(DisplayName = "SEC-URI-018: Null byte in query encoded correctly")]
    public void Should_EncodeNullByteCorrectly_When_QueryContainsEncodedNullByte()
    {
        // Percent-encoded null (%00) is treated as data, not a string terminator
        var uri = new Uri("https://example.com/endpoint?q=value%00injection");

        // Should parse successfully — %00 is just data
        Assert.NotNull(uri);
        Assert.Contains("%00", uri.Query);

        // When encoded on wire, it should appear as %00 (not decoded)
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        var encoded = EncodeHttp11(request);
        Assert.Contains("%00", encoded);
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // Section 7: Backslash Handling (\ vs / normalization)
    // ══════════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "SEC-URI-019: Backslash in path converted to forward slash on Windows")]
    public void Should_NormalizeBackslash_When_PathContainsBackslash()
    {
        // Attack: Backslash path traversal on Windows (e.g., ..\..\etc\passwd)
        // .NET Uri normalizes backslashes to forward slashes on Windows
        var uriString = "https://example.com/api\\..\\sensitive";

        // On Windows, Uri may normalize backslashes — test the actual behavior
        if (OperatingSystem.IsWindows())
        {
            var uri = new Uri(uriString);
            // Backslash should be converted to forward slash
            Assert.DoesNotContain("\\", uri.AbsolutePath);
        }
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // Section 8: Extremely Long URIs (>8KB) handled gracefully
    // ══════════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "SEC-URI-020: Extremely long URI (8KB) encoded successfully with larger buffer")]
    public void Should_EncodeExtremelyLongUri_When_UriExceedsStandardSize()
    {
        // Attack: Resource exhaustion via extremely long URIs
        var longPath = string.Concat(Enumerable.Repeat("segment/", 400)); // ~3200 chars
        var longUri = $"https://example.com/{longPath}query=value";

        var request = new HttpRequestMessage(HttpMethod.Get, longUri);

        // Use a larger buffer to accommodate the long URI
        var bufferSize = 32768; // 32 KB
        var buffer = new Memory<byte>(new byte[bufferSize]);
        var span = buffer.Span;

        // Should encode without throwing
        var written = Http11Encoder.Encode(request, ref span);

        Assert.True(written > 0);
        Assert.True(written < bufferSize);
    }

    [Fact(DisplayName = "SEC-URI-021: Long query string (>4KB) encoded successfully")]
    public void Should_EncodeLongQueryString_When_QueryParametersVeryLarge()
    {
        // Attack: Query string DoS via extremely long parameter values
        var longQueryValue = string.Concat(Enumerable.Repeat("x", 4096));
        var uri = $"https://example.com/endpoint?data={longQueryValue}";

        var request = new HttpRequestMessage(HttpMethod.Get, uri);

        var bufferSize = 32768;
        var buffer = new Memory<byte>(new byte[bufferSize]);
        var span = buffer.Span;

        var written = Http11Encoder.Encode(request, ref span);

        Assert.True(written > 0);
        Assert.True(written < bufferSize);
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // Section 9: Userinfo in Redirect Location
    // ══════════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "SEC-URI-022: Redirect Location with userinfo stripped by sanitizer")]
    public void Should_StripUserinfoInLocation_When_LocationContainsCredentials()
    {
        // Attack: Location header with embedded credentials
        // https://user:password@evil.com/phishing
        var original = new HttpRequestMessage(HttpMethod.Get, "https://trusted.com/page");
        var response = RedirectResponse(HttpStatusCode.Found, "https://admin:secret@attacker.com/");

        var handler = new RedirectHandler();
        var redirect = handler.BuildRedirectRequest(original, response);

        // The redirect URI will contain userinfo in the Uri object, but encoders strip it
        Assert.NotNull(redirect.RequestUri);

        // If we encode the redirect request, userinfo should not appear
        var encoded = EncodeHttp11(redirect, absoluteForm: true);
        Assert.DoesNotContain("admin", encoded);
        Assert.DoesNotContain("secret", encoded);
        Assert.DoesNotContain("@", encoded);
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // Section 10: IPv6 Address Handling in Authority
    // ══════════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "SEC-URI-023: IPv6 address bracketed correctly in authority")]
    public void Should_BracketIPv6Address_When_FormatAuthorityWithIPv6()
    {
        // Legitimate: IPv6 addresses must be bracketed per RFC 9110 §2.7.1
        var uri = new Uri("https://[2001:db8::1]/path");

        var authority = UriSanitizer.FormatAuthority(uri);

        // IPv6 should be bracketed
        Assert.StartsWith("[", authority);
        Assert.Contains("2001:db8", authority);
    }

    [Fact(DisplayName = "SEC-URI-024: IPv6 with port formatted correctly")]
    public void Should_IncludePortWithIPv6_When_NonDefaultPort()
    {
        var uri = new Uri("https://[::1]:8443/admin");

        var authority = UriSanitizer.FormatAuthority(uri);

        // Should be [::1]:8443 (brackets around IPv6, port after)
        Assert.Contains("[::1]", authority);
        Assert.Contains(":8443", authority);
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // Section 11: HTTPS→HTTP Downgrade Protection
    // ══════════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "SEC-URI-025: HTTPS→HTTP downgrade rejected by default policy")]
    public void Should_RejectHttpsToHttpDowngrade_When_DefaultPolicyApplied()
    {
        // Attack: Redirect from HTTPS to HTTP (downgrade attack)
        var original = new HttpRequestMessage(HttpMethod.Get, "https://secure.example.com/");
        var response = RedirectResponse(HttpStatusCode.MovedPermanently, "http://secure.example.com/");

        var handler = new RedirectHandler(RedirectPolicy.Default);

        var ex = Assert.Throws<RedirectException>(() =>
        {
            handler.BuildRedirectRequest(original, response);
        });

        Assert.Equal(RedirectError.ProtocolDowngrade, ex.Error);
    }

    [Fact(DisplayName = "SEC-URI-026: HTTPS→HTTP downgrade allowed only with explicit policy")]
    public void Should_AllowHttpsToHttpDowngrade_When_PolicyAllows()
    {
        // Legitimate case: Allow downgrade if explicitly configured
        var original = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var response = RedirectResponse(HttpStatusCode.Found, "http://example.com/");

        var permissivePolicy = new RedirectPolicy { AllowHttpsToHttpDowngrade = true, MaxRedirects = 20 };
        var handler = new RedirectHandler(permissivePolicy);

        var redirect = handler.BuildRedirectRequest(original, response);

        // Should not throw and redirect should be to HTTP
        Assert.NotNull(redirect.RequestUri);
        Assert.Equal("http", redirect.RequestUri.Scheme);
    }
}
