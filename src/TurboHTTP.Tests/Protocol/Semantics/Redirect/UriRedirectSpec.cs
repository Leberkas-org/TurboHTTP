using System.Net;
using Akka.Actor;
using TurboHTTP.Protocol.Semantics;
using TurboHTTP.Protocol.Syntax.Http11.Client;
using TurboHTTP.Protocol.Syntax.Http11.Options;

namespace TurboHTTP.Tests.Protocol.Semantics.Redirect;

public sealed class UriRedirectSpec
{
    private static readonly Http11ClientEncoder Encoder = new(Http11ClientEncoderOptions.Default);

    private static string EncodeHttp11(HttpRequestMessage request, int bufferSize = 16384)
    {
        var buffer = new byte[bufferSize];
        var written = Encoder.Encode(buffer, request, ActorRefs.Nobody);
        return System.Text.Encoding.ASCII.GetString(buffer, 0, written);
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
        const string uriString = "https://example.com/api\\..\\sensitive";

        if (OperatingSystem.IsWindows())
        {
            var uri = new Uri(uriString);
            Assert.DoesNotContain("\\", uri.AbsolutePath);
        }
    }

    [Fact(Timeout = 5000)]
    public void Http11Encoder_should_encode_extremely_long_uri_when_uri_exceeds_standard_size()
    {
        var longPath = string.Concat(Enumerable.Repeat("segment/", 400));
        var longUri = $"https://example.com/{longPath}query=value";

        var request = new HttpRequestMessage(HttpMethod.Get, longUri);

        const int bufferSize = 32768;
        var written = Encoder.Encode(new byte[bufferSize], request, ActorRefs.Nobody);

        Assert.True(written > 0);
        Assert.True(written < bufferSize);
    }

    [Fact(Timeout = 5000)]
    public void Http11Encoder_should_encode_long_query_string_when_query_parameters_very_large()
    {
        var longQueryValue = string.Concat(Enumerable.Repeat("x", 4096));
        var uri = $"https://example.com/endpoint?data={longQueryValue}";

        var request = new HttpRequestMessage(HttpMethod.Get, uri);

        const int bufferSize = 32768;
        var written = Encoder.Encode(new byte[bufferSize], request, ActorRefs.Nobody);

        Assert.True(written > 0);
        Assert.True(written < bufferSize);
    }

    [Fact(Timeout = 5000)]
    public void RedirectHandler_should_strip_userinfo_in_location_when_location_contains_credentials()
    {
        var original = new HttpRequestMessage(HttpMethod.Get, "https://trusted.com/page");
        var response = RedirectResponse(HttpStatusCode.Found, "https://admin:secret@attacker.com/");

        var handler = new RedirectHandler();
        var redirect = handler.BuildRedirectRequest(original, response);

        Assert.NotNull(redirect.RequestUri);

        var encoded = EncodeHttp11(redirect);
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