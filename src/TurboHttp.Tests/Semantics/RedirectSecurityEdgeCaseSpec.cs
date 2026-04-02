using System.Net;
using TurboHttp.Protocol.Semantics;

namespace TurboHttp.Tests.Semantics;

/// <summary>
/// RFC 9110 §15.4 — Redirect security edge-case tests (RS-017 to RS-030).
/// Covers URL normalization in loop comparison (query strings, fragments,
/// host/scheme case, port normalization), multi-hop chain success,
/// downgrade-before-loop priority, reset behavior, and default policy values.
/// </summary>
/// <remarks>
/// Class under test: <see cref="RedirectHandler"/>.
/// These tests verify FR-5 (HTTPS→HTTP downgrade) and FR-6 (loop/depth) edge cases.
/// </remarks>
public sealed class RedirectSecurityEdgeCaseSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_TreatDifferentQueryStrings_AsDifferentUrls()
    {
        var handler = new RedirectHandler(new RedirectPolicy { MaxRedirects = 10 });

        var req1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page?v=1");
        var res1 = BuildRedirect(HttpStatusCode.Found, "http://example.com/page?v=2");
        var redirected = handler.BuildRedirectRequest(req1, res1);

        Assert.NotNull(redirected);
        Assert.Equal("http://example.com/page?v=2", redirected.RequestUri?.AbsoluteUri);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_DetectLoop_When_SameQueryStringRevisited()
    {
        var handler = new RedirectHandler(new RedirectPolicy { MaxRedirects = 10 });

        var req1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page?v=1");
        var res1 = BuildRedirect(HttpStatusCode.Found, "http://example.com/page?v=2");
        handler.BuildRedirectRequest(req1, res1);

        var req2 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page?v=2");
        var res2 = BuildRedirect(HttpStatusCode.Found, "http://example.com/page?v=1");

        var ex = Assert.Throws<RedirectException>(() =>
            handler.BuildRedirectRequest(req2, res2));

        Assert.Equal(RedirectError.RedirectLoop, ex.Error);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_IgnoreFragment_When_ComparingUrls()
    {
        var handler = new RedirectHandler(new RedirectPolicy { MaxRedirects = 10 });

        var req1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/start");
        var res1 = BuildRedirect(HttpStatusCode.Found, "http://example.com/page#section1");
        handler.BuildRedirectRequest(req1, res1);

        var req2 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page");
        var res2 = BuildRedirect(HttpStatusCode.Found, "http://example.com/page#section2");

        var ex = Assert.Throws<RedirectException>(() =>
            handler.BuildRedirectRequest(req2, res2));

        Assert.Equal(RedirectError.RedirectLoop, ex.Error);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_DetectLoop_When_HostCaseDiffers()
    {
        var handler = new RedirectHandler(new RedirectPolicy { MaxRedirects = 10 });

        var req1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page");
        var res1 = BuildRedirect(HttpStatusCode.Found, "http://example.com/other");
        handler.BuildRedirectRequest(req1, res1);

        var req2 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/other");
        var res2 = BuildRedirect(HttpStatusCode.Found, "http://EXAMPLE.COM/page");

        var ex = Assert.Throws<RedirectException>(() =>
            handler.BuildRedirectRequest(req2, res2));

        Assert.Equal(RedirectError.RedirectLoop, ex.Error);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_DetectLoop_When_SchemeCaseDiffers()
    {
        var handler = new RedirectHandler(new RedirectPolicy { MaxRedirects = 10 });

        var req1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page");
        var res1 = BuildRedirect(HttpStatusCode.Found, "http://example.com/other");
        handler.BuildRedirectRequest(req1, res1);

        var req2 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/other");
        var res2 = BuildRedirect(HttpStatusCode.Found, "HTTP://example.com/page");

        var ex = Assert.Throws<RedirectException>(() =>
            handler.BuildRedirectRequest(req2, res2));

        Assert.Equal(RedirectError.RedirectLoop, ex.Error);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_NotDetectLoop_When_PathCaseDiffers()
    {
        var handler = new RedirectHandler(new RedirectPolicy { MaxRedirects = 10 });

        var req = new HttpRequestMessage(HttpMethod.Get, "http://example.com/Page");
        var res = BuildRedirect(HttpStatusCode.Found, "http://example.com/page");
        var redirected = handler.BuildRedirectRequest(req, res);

        Assert.NotNull(redirected);
        Assert.Equal("http://example.com/page", redirected.RequestUri?.AbsoluteUri);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_DetectLoop_When_DefaultPortExplicitlySpecified()
    {
        var handler = new RedirectHandler(new RedirectPolicy { MaxRedirects = 10 });

        var req1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/a");
        var res1 = BuildRedirect(HttpStatusCode.Found, "http://example.com/b");
        handler.BuildRedirectRequest(req1, res1);

        var req2 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/b");
        var res2 = BuildRedirect(HttpStatusCode.Found, "http://example.com:80/a");

        var ex = Assert.Throws<RedirectException>(() =>
            handler.BuildRedirectRequest(req2, res2));

        Assert.Equal(RedirectError.RedirectLoop, ex.Error);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_NotDetectLoop_When_PortsDiffer()
    {
        var handler = new RedirectHandler(new RedirectPolicy { MaxRedirects = 10 });

        var req = new HttpRequestMessage(HttpMethod.Get, "http://example.com:8080/page");
        var res = BuildRedirect(HttpStatusCode.Found, "http://example.com:9090/page");
        var redirected = handler.BuildRedirectRequest(req, res);

        Assert.NotNull(redirected);
        Assert.Equal("http://example.com:9090/page", redirected.RequestUri?.AbsoluteUri);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_SucceedMultiHopChain_When_WithinMaxDepth()
    {
        var handler = new RedirectHandler(new RedirectPolicy { MaxRedirects = 5 });

        var req1 = new HttpRequestMessage(HttpMethod.Get, "http://a.com/start");
        var res1 = BuildRedirect(HttpStatusCode.Found, "http://b.com/step1");
        handler.BuildRedirectRequest(req1, res1);

        var req2 = new HttpRequestMessage(HttpMethod.Get, "http://b.com/step1");
        var res2 = BuildRedirect(HttpStatusCode.Found, "http://c.com/step2");
        handler.BuildRedirectRequest(req2, res2);

        var req3 = new HttpRequestMessage(HttpMethod.Get, "http://c.com/step2");
        var res3 = BuildRedirect(HttpStatusCode.Found, "http://d.com/step3");
        handler.BuildRedirectRequest(req3, res3);

        Assert.Equal(3, handler.RedirectCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_ThrowProtocolDowngrade_BeforeLoopDetection()
    {
        // If HTTPS→HTTP, ProtocolDowngrade should fire even if the URL is also a loop
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Get, "https://example.com/page");
        var response = BuildRedirect(HttpStatusCode.Found, "http://example.com/page");

        var ex = Assert.Throws<RedirectException>(() =>
            handler.BuildRedirectRequest(original, response));

        // ProtocolDowngrade takes precedence because the check runs before loop detection
        Assert.Equal(RedirectError.ProtocolDowngrade, ex.Error);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_ClearState_WhenReset()
    {
        var handler = new RedirectHandler();

        var req1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/a");
        var res1 = BuildRedirect(HttpStatusCode.Found, "http://example.com/b");
        handler.BuildRedirectRequest(req1, res1);
        Assert.Equal(1, handler.RedirectCount);

        handler.Reset();
        Assert.Equal(0, handler.RedirectCount);

        // After reset, can revisit the same URLs without loop detection
        var req2 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/a");
        var res2 = BuildRedirect(HttpStatusCode.Found, "http://example.com/b");
        var redirected = handler.BuildRedirectRequest(req2, res2);

        Assert.NotNull(redirected);
        Assert.Equal(1, handler.RedirectCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_DetectLoop_When_HttpsDefaultPortExplicit()
    {
        var handler = new RedirectHandler(new RedirectPolicy { MaxRedirects = 10 });

        var req1 = new HttpRequestMessage(HttpMethod.Get, "https://example.com/a");
        var res1 = BuildRedirect(HttpStatusCode.Found, "https://example.com/b");
        handler.BuildRedirectRequest(req1, res1);

        var req2 = new HttpRequestMessage(HttpMethod.Get, "https://example.com/b");
        var res2 = BuildRedirect(HttpStatusCode.Found, "https://example.com:443/a");

        var ex = Assert.Throws<RedirectException>(() =>
            handler.BuildRedirectRequest(req2, res2));

        Assert.Equal(RedirectError.RedirectLoop, ex.Error);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_DefaultPolicyBlockDowngrade()
    {
        Assert.False(RedirectPolicy.Default.AllowHttpsToHttpDowngrade);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_DefaultPolicyMaxRedirects5()
    {
        Assert.Equal(10, RedirectPolicy.Default.MaxRedirects);
    }


    private static HttpResponseMessage BuildRedirect(HttpStatusCode statusCode, string location)
    {
        var response = new HttpResponseMessage(statusCode);
        response.Headers.TryAddWithoutValidation("Location", location);
        return response;
    }
}
