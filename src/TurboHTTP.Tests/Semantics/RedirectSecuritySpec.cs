using System.Net;
using TurboHTTP.Protocol.Semantics;

namespace TurboHTTP.Tests.Semantics;

/// <summary>
/// RFC 9110 §15.4 — Redirect security tests (RS-001 to RS-016).
/// Covers HTTPS→HTTP downgrade protection, redirect loop detection,
/// and max redirect depth enforcement.
/// </summary>
/// <remarks>
/// Class under test: <see cref="RedirectHandler"/>.
/// These tests verify FR-5 (HTTPS→HTTP downgrade) and FR-6 (loop/depth).
/// </remarks>
public sealed class RedirectSecuritySpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_RejectDowngrade_When_HttpsToHttp_DefaultPolicy()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Get, "https://example.com/secure");
        var response = BuildRedirect(HttpStatusCode.Found, "http://example.com/insecure");

        var ex = Assert.Throws<RedirectException>(() =>
            handler.BuildRedirectRequest(original, response));

        Assert.Equal(RedirectError.ProtocolDowngrade, ex.Error);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_AllowDowngrade_When_PolicyPermits()
    {
        var handler = new RedirectHandler(new RedirectPolicy { AllowHttpsToHttpDowngrade = true });
        var original = new HttpRequestMessage(HttpMethod.Get, "https://example.com/secure");
        var response = BuildRedirect(HttpStatusCode.Found, "http://example.com/page");

        var redirected = handler.BuildRedirectRequest(original, response);

        Assert.NotNull(redirected);
        Assert.Equal("http://example.com/page", redirected.RequestUri?.AbsoluteUri);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_AllowRedirect_When_HttpsToHttps()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Get, "https://example.com/page");
        var response = BuildRedirect(HttpStatusCode.Found, "https://example.com/other");

        var redirected = handler.BuildRedirectRequest(original, response);

        Assert.NotNull(redirected);
        Assert.Equal("https://example.com/other", redirected.RequestUri?.AbsoluteUri);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_AllowRedirect_When_HttpToHttp()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page");
        var response = BuildRedirect(HttpStatusCode.Found, "http://example.com/other");

        var redirected = handler.BuildRedirectRequest(original, response);

        Assert.NotNull(redirected);
        Assert.Equal("http://example.com/other", redirected.RequestUri?.AbsoluteUri);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_AllowRedirect_When_HttpToHttps()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page");
        var response = BuildRedirect(HttpStatusCode.Found, "https://example.com/page");

        var redirected = handler.BuildRedirectRequest(original, response);

        Assert.NotNull(redirected);
        Assert.Equal("https://example.com/page", redirected.RequestUri?.AbsoluteUri);
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    [InlineData(HttpStatusCode.MovedPermanently)]
    [InlineData(HttpStatusCode.Found)]
    [InlineData(HttpStatusCode.SeeOther)]
    [InlineData(HttpStatusCode.TemporaryRedirect)]
    [InlineData(HttpStatusCode.PermanentRedirect)]
    public void Should_BlockDowngrade_ForAllRedirectStatusCodes(HttpStatusCode statusCode)
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Get, "https://example.com/secure");
        var response = BuildRedirect(statusCode, "http://example.com/insecure");

        var ex = Assert.Throws<RedirectException>(() =>
            handler.BuildRedirectRequest(original, response));

        Assert.Equal(RedirectError.ProtocolDowngrade, ex.Error);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_IncludeBlockedInMessage_When_DowngradeRejected()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Get, "https://example.com/page");
        var response = BuildRedirect(HttpStatusCode.Found, "http://example.com/page");

        var ex = Assert.Throws<RedirectException>(() =>
            handler.BuildRedirectRequest(original, response));

        Assert.Contains("RFC 9110 §15.4: Redirect from HTTPS to HTTP is not", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_BlockDowngrade_When_CrossOriginHttpsToHttp()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Get, "https://secure.example.com/page");
        var response = BuildRedirect(HttpStatusCode.Found, "http://insecure.example.com/page");

        var ex = Assert.Throws<RedirectException>(() =>
            handler.BuildRedirectRequest(original, response));

        Assert.Equal(RedirectError.ProtocolDowngrade, ex.Error);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_DetectLoop_When_SelfRedirect()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page");
        var response = BuildRedirect(HttpStatusCode.Found, "http://example.com/page");

        var ex = Assert.Throws<RedirectException>(() =>
            handler.BuildRedirectRequest(original, response));

        Assert.Equal(RedirectError.RedirectLoop, ex.Error);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_DetectLoop_When_TwoStepCycle()
    {
        var handler = new RedirectHandler();

        var req1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/a");
        var res1 = BuildRedirect(HttpStatusCode.Found, "http://example.com/b");
        handler.BuildRedirectRequest(req1, res1);

        var req2 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/b");
        var res2 = BuildRedirect(HttpStatusCode.Found, "http://example.com/a");

        var ex = Assert.Throws<RedirectException>(() =>
            handler.BuildRedirectRequest(req2, res2));

        Assert.Equal(RedirectError.RedirectLoop, ex.Error);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_DetectLoop_When_ThreeStepRevisit()
    {
        var handler = new RedirectHandler(new RedirectPolicy { MaxRedirects = 10 });

        var req1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/a");
        var res1 = BuildRedirect(HttpStatusCode.Found, "http://example.com/b");
        handler.BuildRedirectRequest(req1, res1);

        var req2 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/b");
        var res2 = BuildRedirect(HttpStatusCode.Found, "http://example.com/c");
        handler.BuildRedirectRequest(req2, res2);

        var req3 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/c");
        var res3 = BuildRedirect(HttpStatusCode.Found, "http://example.com/b");

        var ex = Assert.Throws<RedirectException>(() =>
            handler.BuildRedirectRequest(req3, res3));

        Assert.Equal(RedirectError.RedirectLoop, ex.Error);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_IncludeTargetUri_InLoopErrorMessage()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/target");
        var response = BuildRedirect(HttpStatusCode.Found, "http://example.com/target");

        var ex = Assert.Throws<RedirectException>(() =>
            handler.BuildRedirectRequest(original, response));

        Assert.Contains("example.com/target", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_AcceptExactly5Redirects_WithDefaultPolicy()
    {
        var handler = new RedirectHandler();

        for (var i = 0; i < 5; i++)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, $"http://example.com/p{i}");
            var res = BuildRedirect(HttpStatusCode.Found, $"http://example.com/p{i + 1}");
            handler.BuildRedirectRequest(req, res);
        }

        Assert.Equal(5, handler.RedirectCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_Reject6thRedirect_WithDefaultPolicy()
    {
        var handler = new RedirectHandler();

        for (var i = 0; i < 10; i++)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, $"http://example.com/p{i}");
            var res = BuildRedirect(HttpStatusCode.Found, $"http://example.com/p{i + 1}");
            handler.BuildRedirectRequest(req, res);
        }

        var extra = new HttpRequestMessage(HttpMethod.Get, "http://example.com/p5");
        var extraRes = BuildRedirect(HttpStatusCode.Found, "http://example.com/p6");

        var ex = Assert.Throws<RedirectException>(() =>
            handler.BuildRedirectRequest(extra, extraRes));

        Assert.Equal(RedirectError.MaxRedirectsExceeded, ex.Error);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_IncludeLimit_InMaxDepthErrorMessage()
    {
        var handler = new RedirectHandler(new RedirectPolicy { MaxRedirects = 3 });

        for (var i = 0; i < 3; i++)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, $"http://example.com/p{i}");
            var res = BuildRedirect(HttpStatusCode.Found, $"http://example.com/p{i + 1}");
            handler.BuildRedirectRequest(req, res);
        }

        var extra = new HttpRequestMessage(HttpMethod.Get, "http://example.com/p3");
        var extraRes = BuildRedirect(HttpStatusCode.Found, "http://example.com/p4");

        var ex = Assert.Throws<RedirectException>(() =>
            handler.BuildRedirectRequest(extra, extraRes));

        Assert.Contains("3", ex.Message);
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(10)]
    public void Should_RespectCustomMaxDepth(int maxRedirects)
    {
        var handler = new RedirectHandler(new RedirectPolicy { MaxRedirects = maxRedirects });

        for (var i = 0; i < maxRedirects; i++)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, $"http://example.com/p{i}");
            var res = BuildRedirect(HttpStatusCode.Found, $"http://example.com/p{i + 1}");
            handler.BuildRedirectRequest(req, res);
        }

        Assert.Equal(maxRedirects, handler.RedirectCount);

        var extra = new HttpRequestMessage(HttpMethod.Get, $"http://example.com/p{maxRedirects}");
        var extraRes = BuildRedirect(HttpStatusCode.Found, $"http://example.com/p{maxRedirects + 1}");

        var ex = Assert.Throws<RedirectException>(() =>
            handler.BuildRedirectRequest(extra, extraRes));

        Assert.Equal(RedirectError.MaxRedirectsExceeded, ex.Error);
    }


    private static HttpResponseMessage BuildRedirect(HttpStatusCode statusCode, string location)
    {
        var response = new HttpResponseMessage(statusCode);
        response.Headers.TryAddWithoutValidation("Location", location);
        return response;
    }
}
