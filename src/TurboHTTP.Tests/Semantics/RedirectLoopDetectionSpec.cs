using System.Net;
using TurboHTTP.Protocol.Semantics;

namespace TurboHTTP.Tests.Semantics;

/// <summary>
/// RFC 9110 §15.4 — Redirect loop detection and depth limit tests.
/// Covers redirect loop detection with URI normalization (case-insensitive scheme/host,
/// case-sensitive path), max redirect depth enforcement, query string/fragment handling,
/// and relative URL resolution.
/// </summary>
/// <remarks>
/// Class under test: <see cref="RedirectHandler"/>.
/// These tests verify FR-6: redirect chains that revisit the same URL or exceed max depth are rejected.
/// </remarks>
public sealed class RedirectLoopDetectionSpec
{

    [Fact]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_DetectLoop_When_SelfRedirect()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page");
        var response = BuildRedirect(HttpStatusCode.Found, "http://example.com/page");

        var ex = Assert.Throws<RedirectException>(() =>
            handler.BuildRedirectRequest(original, response));

        Assert.Equal(RedirectError.RedirectLoop, ex.Error);
        Assert.Contains("RFC 9110 §15.4: Redirect loop detected.", ex.Message);
    }

    [Fact]
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

    [Fact]
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


    [Fact]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_AllowExactly5Redirects_When_UsingDefaultPolicy()
    {
        var handler = new RedirectHandler(); // default MaxRedirects = 5

        for (var i = 0; i < 5; i++)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, $"http://example.com/p{i}");
            var res = BuildRedirect(HttpStatusCode.Found, $"http://example.com/p{i + 1}");
            handler.BuildRedirectRequest(req, res);
        }

        Assert.Equal(5, handler.RedirectCount);
    }

    [Fact]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_ThrowMaxDepth_When_SixthRedirectWithDefaultPolicy()
    {
        var handler = new RedirectHandler(); // default MaxRedirects = 10

        for (var i = 0; i < 10; i++)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, $"http://example.com/p{i}");
            var res = BuildRedirect(HttpStatusCode.Found, $"http://example.com/p{i + 1}");
            handler.BuildRedirectRequest(req, res);
        }

        var final = new HttpRequestMessage(HttpMethod.Get, "http://example.com/p5");
        var finalRes = BuildRedirect(HttpStatusCode.Found, "http://example.com/p6");

        var ex = Assert.Throws<RedirectException>(() =>
            handler.BuildRedirectRequest(final, finalRes));

        Assert.Equal(RedirectError.MaxRedirectsExceeded, ex.Error);
        Assert.Contains("RFC 9110 §15.4: Maximum redirect limit of", ex.Message);
        Assert.Contains("10", ex.Message);
    }

    [Theory]
    [Trait("RFC", "RFC9110-15.4")]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(10)]
    [InlineData(20)]
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

        // Next redirect should throw
        var extra = new HttpRequestMessage(HttpMethod.Get, $"http://example.com/p{maxRedirects}");
        var extraRes = BuildRedirect(HttpStatusCode.Found, $"http://example.com/p{maxRedirects + 1}");

        var ex = Assert.Throws<RedirectException>(() =>
            handler.BuildRedirectRequest(extra, extraRes));

        Assert.Equal(RedirectError.MaxRedirectsExceeded, ex.Error);
    }

    [Fact]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_EnforceMaxDepth_When_AllUrlsDiffer()
    {
        var handler = new RedirectHandler(new RedirectPolicy { MaxRedirects = 2 });

        var req1 = new HttpRequestMessage(HttpMethod.Get, "http://a.com/1");
        var res1 = BuildRedirect(HttpStatusCode.Found, "http://b.com/2");
        handler.BuildRedirectRequest(req1, res1);

        var req2 = new HttpRequestMessage(HttpMethod.Get, "http://b.com/2");
        var res2 = BuildRedirect(HttpStatusCode.Found, "http://c.com/3");
        handler.BuildRedirectRequest(req2, res2);

        var req3 = new HttpRequestMessage(HttpMethod.Get, "http://c.com/3");
        var res3 = BuildRedirect(HttpStatusCode.Found, "http://d.com/4");

        var ex = Assert.Throws<RedirectException>(() =>
            handler.BuildRedirectRequest(req3, res3));

        Assert.Equal(RedirectError.MaxRedirectsExceeded, ex.Error);
    }


    [Fact]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_TreatQueryStringVariationsAsDifferent()
    {
        var handler = new RedirectHandler(new RedirectPolicy { MaxRedirects = 10 });

        var req1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page?v=1");
        var res1 = BuildRedirect(HttpStatusCode.Found, "http://example.com/page?v=2");
        var redirected = handler.BuildRedirectRequest(req1, res1);

        Assert.NotNull(redirected);
        Assert.Equal("http://example.com/page?v=2", redirected.RequestUri?.AbsoluteUri);
    }

    [Fact]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_NotDetectLoop_When_QueryStringsDiffer()
    {
        var handler = new RedirectHandler(new RedirectPolicy { MaxRedirects = 10 });

        var req1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page?v=1");
        var res1 = BuildRedirect(HttpStatusCode.Found, "http://example.com/page?v=2");
        handler.BuildRedirectRequest(req1, res1);

        var req2 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page?v=2");
        var res2 = BuildRedirect(HttpStatusCode.Found, "http://example.com/page?v=3");
        var redirected = handler.BuildRedirectRequest(req2, res2);

        Assert.NotNull(redirected);
    }

    [Fact]
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

    [Fact]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_IgnoreFragments_When_ComparingUrls()
    {
        var handler = new RedirectHandler(new RedirectPolicy { MaxRedirects = 10 });

        // First redirect to /page#section1
        var req1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/start");
        var res1 = BuildRedirect(HttpStatusCode.Found, "http://example.com/page#section1");
        handler.BuildRedirectRequest(req1, res1);

        // Now redirect to /page#section2 — same path, different fragment → loop because fragments are ignored
        var req2 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page");
        var res2 = BuildRedirect(HttpStatusCode.Found, "http://example.com/page#section2");

        var ex = Assert.Throws<RedirectException>(() =>
            handler.BuildRedirectRequest(req2, res2));

        Assert.Equal(RedirectError.RedirectLoop, ex.Error);
    }


    [Fact]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_DetectLoop_When_SchemesCaseDiffers()
    {
        var handler = new RedirectHandler(new RedirectPolicy { MaxRedirects = 10 });

        var req1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page");
        var res1 = BuildRedirect(HttpStatusCode.Found, "http://example.com/other");
        handler.BuildRedirectRequest(req1, res1);

        // Redirect back to /page with HTTP uppercase — should be treated as same URL
        var req2 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/other");
        var res2 = BuildRedirect(HttpStatusCode.Found, "HTTP://example.com/page");

        var ex = Assert.Throws<RedirectException>(() =>
            handler.BuildRedirectRequest(req2, res2));

        Assert.Equal(RedirectError.RedirectLoop, ex.Error);
    }

    [Fact]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_DetectLoop_When_HostCaseDiffers()
    {
        var handler = new RedirectHandler(new RedirectPolicy { MaxRedirects = 10 });

        var req1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page");
        var res1 = BuildRedirect(HttpStatusCode.Found, "http://example.com/other");
        handler.BuildRedirectRequest(req1, res1);

        // Redirect back to /page with EXAMPLE.COM — should be treated as same URL
        var req2 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/other");
        var res2 = BuildRedirect(HttpStatusCode.Found, "http://EXAMPLE.COM/page");

        var ex = Assert.Throws<RedirectException>(() =>
            handler.BuildRedirectRequest(req2, res2));

        Assert.Equal(RedirectError.RedirectLoop, ex.Error);
    }

    [Fact]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_NotDetectLoop_When_PathCaseDiffers()
    {
        var handler = new RedirectHandler(new RedirectPolicy { MaxRedirects = 10 });

        var req1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/Page");
        var res1 = BuildRedirect(HttpStatusCode.Found, "http://example.com/page");
        var redirected = handler.BuildRedirectRequest(req1, res1);

        // /Page and /page are different URIs (path is case-sensitive)
        Assert.NotNull(redirected);
        Assert.Equal("http://example.com/page", redirected.RequestUri?.AbsoluteUri);
    }

    [Fact]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_DetectLoop_When_MixedCaseHostSamePath()
    {
        var handler = new RedirectHandler(new RedirectPolicy { MaxRedirects = 10 });

        // Start at Example.Com/path, redirect to example.com/other
        var req1 = new HttpRequestMessage(HttpMethod.Get, "http://Example.Com/path");
        var res1 = BuildRedirect(HttpStatusCode.Found, "http://example.com/other");
        handler.BuildRedirectRequest(req1, res1);

        // Redirect back to EXAMPLE.COM/path — same as starting URL after host normalization
        var req2 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/other");
        var res2 = BuildRedirect(HttpStatusCode.Found, "http://EXAMPLE.COM/path");

        var ex = Assert.Throws<RedirectException>(() =>
            handler.BuildRedirectRequest(req2, res2));

        Assert.Equal(RedirectError.RedirectLoop, ex.Error);
    }


    [Fact]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_DetectLoop_When_RedirectResolvesToVisitedUrl()
    {
        var handler = new RedirectHandler(new RedirectPolicy { MaxRedirects = 10 });

        var req1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/a");
        var res1 = BuildRedirect(HttpStatusCode.Found, "http://example.com/b");
        handler.BuildRedirectRequest(req1, res1);

        // Redirect back to /a — should detect loop
        var req2 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/b");
        var res2 = BuildRedirect(HttpStatusCode.Found, "http://example.com/a");

        var ex = Assert.Throws<RedirectException>(() =>
            handler.BuildRedirectRequest(req2, res2));

        Assert.Equal(RedirectError.RedirectLoop, ex.Error);
    }

    [Fact]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_FollowRedirect_When_AbsoluteUrlIsNew()
    {
        var handler = new RedirectHandler(new RedirectPolicy { MaxRedirects = 10 });

        var req1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/a");
        var res1 = BuildRedirect(HttpStatusCode.Found, "http://example.com/b");
        var redirected = handler.BuildRedirectRequest(req1, res1);

        Assert.Equal("http://example.com/b", redirected.RequestUri?.AbsoluteUri);
    }


    [Fact]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_ClearState_When_Reset()
    {
        var handler = new RedirectHandler(new RedirectPolicy { MaxRedirects = 10 });

        var req1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/a");
        var res1 = BuildRedirect(HttpStatusCode.Found, "http://example.com/b");
        handler.BuildRedirectRequest(req1, res1);
        Assert.Equal(1, handler.RedirectCount);

        handler.Reset();
        Assert.Equal(0, handler.RedirectCount);

        // After reset, can visit /a and /b again without loop detection
        var req2 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/a");
        var res2 = BuildRedirect(HttpStatusCode.Found, "http://example.com/b");
        var redirected = handler.BuildRedirectRequest(req2, res2);

        Assert.NotNull(redirected);
        Assert.Equal(1, handler.RedirectCount);
    }


    [Fact]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_TreatDifferentPortsAsDifferentUrls()
    {
        var handler = new RedirectHandler(new RedirectPolicy { MaxRedirects = 10 });

        var req1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com:8080/page");
        var res1 = BuildRedirect(HttpStatusCode.Found, "http://example.com:9090/page");
        var redirected = handler.BuildRedirectRequest(req1, res1);

        Assert.NotNull(redirected);
        Assert.Equal("http://example.com:9090/page", redirected.RequestUri?.AbsoluteUri);
    }

    [Fact]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_DetectLoop_When_DefaultPortExplicitlySpecified()
    {
        var handler = new RedirectHandler(new RedirectPolicy { MaxRedirects = 10 });

        // http://example.com/a and http://example.com:80/a should be the same
        var req1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/a");
        var res1 = BuildRedirect(HttpStatusCode.Found, "http://example.com/b");
        handler.BuildRedirectRequest(req1, res1);

        var req2 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/b");
        var res2 = BuildRedirect(HttpStatusCode.Found, "http://example.com:80/a");

        var ex = Assert.Throws<RedirectException>(() =>
            handler.BuildRedirectRequest(req2, res2));

        Assert.Equal(RedirectError.RedirectLoop, ex.Error);
    }


    [Fact]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_IncludeTargetUri_InLoopErrorMessage()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page");
        var response = BuildRedirect(HttpStatusCode.Found, "http://example.com/page");

        var ex = Assert.Throws<RedirectException>(() =>
            handler.BuildRedirectRequest(original, response));

        Assert.Contains("RFC 9110 §15.4: Redirect loop detected. URI alread", ex.Message);
        Assert.Contains("example.com/page", ex.Message);
    }

    [Fact]
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

        Assert.Contains("RFC 9110 §15.4: Maximum redirect limit of", ex.Message);
        Assert.Contains("3", ex.Message);
    }


    private static HttpResponseMessage BuildRedirect(HttpStatusCode statusCode, string location)
    {
        var response = new HttpResponseMessage(statusCode);
        response.Headers.TryAddWithoutValidation("Location", location);
        return response;
    }
}
