using System.Net;
using System.Text;
using TurboHTTP.Protocol.Semantics;
using Encoder = TurboHTTP.Protocol.Http11.Encoder;

namespace TurboHTTP.Tests.Security;

public sealed class UriRedirectSpec
{
    private static string EncodeHttp11(HttpRequestMessage request, bool absoluteForm = false, int bufferSize = 16384)
    {
        var buffer = new Memory<byte>(new byte[bufferSize]);
        var span = buffer.Span;
        var written = Encoder.Encode(request, ref span, absoluteForm);
        return Encoding.ASCII.GetString(buffer.Span[..written]);
    }

    private static HttpResponseMessage RedirectResponse(HttpStatusCode status, string location)
    {
        var response = new HttpResponseMessage(status);
        response.Headers.TryAddWithoutValidation("Location", location);
        return response;
    }

    [Fact(Timeout = 5000)]
    public void Uri_should_normalize_backslash_when_path_contains_backslash()
    {
        // Attack: Backslash path traversal on Windows (e.g., ..\..\etc\passwd)
        // .NET Uri normalizes backslashes to forward slashes on Windows
        const string uriString = "https://example.com/api\\..\\sensitive";

        // On Windows, Uri may normalize backslashes — test the actual behavior
        if (OperatingSystem.IsWindows())
        {
            var uri = new Uri(uriString);
            // Backslash should be converted to forward slash
            Assert.DoesNotContain("\\", uri.AbsolutePath);
        }
    }

    [Fact(Timeout = 5000)]
    public void Http11Encoder_should_encode_extremely_long_uri_when_uri_exceeds_standard_size()
    {
        // Attack: Resource exhaustion via extremely long URIs
        var longPath = string.Concat(Enumerable.Repeat("segment/", 400)); // ~3200 chars
        var longUri = $"https://example.com/{longPath}query=value";

        var request = new HttpRequestMessage(HttpMethod.Get, longUri);

        // Use a larger buffer to accommodate the long URI
        const int bufferSize = 32768; // 32 KB
        var buffer = new Memory<byte>(new byte[bufferSize]);
        var span = buffer.Span;

        // Should encode without throwing
        var written = Encoder.Encode(request, ref span);

        Assert.True(written > 0);
        Assert.True(written < bufferSize);
    }

    [Fact(Timeout = 5000)]
    public void Http11Encoder_should_encode_long_query_string_when_query_parameters_very_large()
    {
        // Attack: Query string DoS via extremely long parameter values
        var longQueryValue = string.Concat(Enumerable.Repeat("x", 4096));
        var uri = $"https://example.com/endpoint?data={longQueryValue}";

        var request = new HttpRequestMessage(HttpMethod.Get, uri);

        const int bufferSize = 32768;
        var buffer = new Memory<byte>(new byte[bufferSize]);
        var span = buffer.Span;

        var written = Encoder.Encode(request, ref span);

        Assert.True(written > 0);
        Assert.True(written < bufferSize);
    }

    [Fact(Timeout = 5000)]
    public void RedirectHandler_should_strip_userinfo_in_location_when_location_contains_credentials()
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

    [Fact(Timeout = 5000)]
    public void UriSanitizer_should_bracket_ipv6_address_when_format_authority_with_ipv6()
    {
        // Legitimate: IPv6 addresses must be bracketed per RFC 9110 §2.7.1
        var uri = new Uri("https://[2001:db8::1]/path");

        var authority = UriSanitizer.FormatAuthority(uri);

        // IPv6 should be bracketed
        Assert.StartsWith("[", authority);
        Assert.Contains("2001:db8", authority);
    }

    [Fact(Timeout = 5000)]
    public void UriSanitizer_should_include_port_with_ipv6_when_non_default_port()
    {
        var uri = new Uri("https://[::1]:8443/admin");

        var authority = UriSanitizer.FormatAuthority(uri);

        // Should be [::1]:8443 (brackets around IPv6, port after)
        Assert.Contains("[::1]", authority);
        Assert.Contains(":8443", authority);
    }

    [Fact(Timeout = 5000)]
    public void RedirectHandler_should_reject_https_to_http_downgrade_when_default_policy_applied()
    {
        // Attack: Redirect from HTTPS to HTTP (downgrade attack)
        var original = new HttpRequestMessage(HttpMethod.Get, "https://secure.example.com/");
        var response = RedirectResponse(HttpStatusCode.MovedPermanently, "http://secure.example.com/");

        var handler = new RedirectHandler(RedirectPolicy.Default);

        var ex = Assert.Throws<RedirectException>(() => { handler.BuildRedirectRequest(original, response); });

        Assert.Equal(RedirectError.ProtocolDowngrade, ex.Error);
    }

    [Fact(Timeout = 5000)]
    public void RedirectHandler_should_allow_https_to_http_downgrade_when_policy_allows()
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