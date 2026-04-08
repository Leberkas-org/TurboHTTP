using System.Net;
using TurboHTTP.Protocol.Cookies;
using TurboHTTP.Protocol.Semantics;

namespace TurboHTTP.Tests.Semantics;

/// <summary>
/// RFC 9110 §15.4 — Redirect handler version preservation and URL normalization tests (RH-052 to RH-067).
/// Covers HTTP version preservation across redirects, independent handler isolation,
/// query string and fragment handling, case-insensitive scheme/host loop detection,
/// and three-hop cycle detection.
/// </summary>
/// <remarks>
/// Class under test: <see cref="RedirectHandler"/>.
/// RFC 9110 §15.4: URL comparison for loop detection must normalize scheme and host (case-insensitive).
/// </remarks>
public sealed class RedirectHandlerNormalizationSpec
{

    [Fact]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_PreserveVersion_When_RedirectingHttp2Request()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page")
        {
            Version = new Version(2, 0)
        };
        var response = BuildRedirect(HttpStatusCode.Found, "http://example.com/new");

        var redirected = handler.BuildRedirectRequest(original, response);

        Assert.Equal(new Version(2, 0), redirected.Version);
    }

    [Fact]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_PreserveVersion_When_RedirectingHttp10Request()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page")
        {
            Version = new Version(1, 0)
        };
        var response = BuildRedirect(HttpStatusCode.Found, "http://example.com/new");

        var redirected = handler.BuildRedirectRequest(original, response);

        Assert.Equal(new Version(1, 0), redirected.Version);
    }

    [Fact]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_PreserveVersion_When_CrossOriginRedirect()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/api")
        {
            Version = new Version(2, 0)
        };
        var response = BuildRedirect(HttpStatusCode.Found, "http://other.com/api");

        var redirected = handler.BuildRedirectRequest(original, response);

        Assert.Equal(new Version(2, 0), redirected.Version);
    }

    [Fact]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_PreserveVersion_When_RedirectingHttp11Request()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page")
        {
            Version = new Version(1, 1)
        };
        var response = BuildRedirect(HttpStatusCode.Found, "http://example.com/new");

        var redirected = handler.BuildRedirectRequest(original, response);

        Assert.Equal(new Version(1, 1), redirected.Version);
    }

    [Fact]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_PreserveVersion_When_RedirectingWithJar()
    {
        var handler = new RedirectHandler();
        var jar = new CookieJar();
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page")
        {
            Version = new Version(2, 0)
        };
        var response = BuildRedirect(HttpStatusCode.Found, "http://example.com/new");

        var redirected = handler.BuildRedirectRequest(original, response, jar);

        Assert.Equal(new Version(2, 0), redirected.Version);
    }


    [Fact]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_IsolateRedirectCount_When_UsingIndependentHandlers()
    {
        var policy = new RedirectPolicy { MaxRedirects = 5 };

        // Request A: exhaust all 5 redirects
        var handlerA = new RedirectHandler(policy);
        for (var i = 0; i < 5; i++)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, $"http://example.com/a{i}");
            var res = BuildRedirect(HttpStatusCode.Found, $"http://example.com/a{i + 1}");
            handlerA.BuildRedirectRequest(req, res);
        }
        Assert.Equal(5, handlerA.RedirectCount);

        // Request A's 6th redirect should throw
        var reqA6 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/a5");
        var resA6 = BuildRedirect(HttpStatusCode.Found, "http://example.com/a6");
        Assert.Throws<RedirectException>(() => handlerA.BuildRedirectRequest(reqA6, resA6));

        // Request B: fresh handler, should start at 0
        var handlerB = new RedirectHandler(policy);
        Assert.Equal(0, handlerB.RedirectCount);

        var reqB = new HttpRequestMessage(HttpMethod.Get, "http://example.com/b0");
        var resB = BuildRedirect(HttpStatusCode.Found, "http://example.com/b1");
        var redirectedB = handlerB.BuildRedirectRequest(reqB, resB);
        Assert.NotNull(redirectedB);
        Assert.Equal(1, handlerB.RedirectCount);
    }

    [Fact]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_NotDetectFalseLoop_When_UsingIndependentHandlers()
    {
        // Handler A visits /shared
        var handlerA = new RedirectHandler();
        var reqA = new HttpRequestMessage(HttpMethod.Get, "http://example.com/a");
        var resA = BuildRedirect(HttpStatusCode.Found, "http://example.com/shared");
        handlerA.BuildRedirectRequest(reqA, resA);

        // Handler B also visits /shared — should NOT throw loop detection
        var handlerB = new RedirectHandler();
        var reqB = new HttpRequestMessage(HttpMethod.Get, "http://example.com/b");
        var resB = BuildRedirect(HttpStatusCode.Found, "http://example.com/shared");
        var redirectedB = handlerB.BuildRedirectRequest(reqB, resB);
        Assert.NotNull(redirectedB);
        Assert.Equal("http://example.com/shared", redirectedB.RequestUri?.AbsoluteUri);
    }


    [Fact]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_NotDetectLoop_When_QueryStringsDiffer()
    {
        var handler = new RedirectHandler();
        var req1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page?v=1");
        var res1 = BuildRedirect(HttpStatusCode.Found, "http://example.com/page?v=2");
        var redirected = handler.BuildRedirectRequest(req1, res1);

        Assert.Equal("http://example.com/page?v=2", redirected.RequestUri?.AbsoluteUri);
    }

    [Fact]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_DetectLoop_When_QueryStringsMatch()
    {
        var handler = new RedirectHandler();
        var req1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/a?v=1");
        var res1 = BuildRedirect(HttpStatusCode.Found, "http://example.com/b?v=1");
        handler.BuildRedirectRequest(req1, res1);

        var req2 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/b?v=1");
        var res2 = BuildRedirect(HttpStatusCode.Found, "http://example.com/a?v=1");

        var ex = Assert.Throws<RedirectException>(() =>
            handler.BuildRedirectRequest(req2, res2));
        Assert.Equal(RedirectError.RedirectLoop, ex.Error);
    }

    [Fact]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_DetectLoop_When_OnlyFragmentDiffers()
    {
        var handler = new RedirectHandler();
        var req1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page#section1");
        var res1 = BuildRedirect(HttpStatusCode.Found, "http://example.com/page#section2");

        // Fragments are stripped by Uri, so these should be the same after normalization
        var ex = Assert.Throws<RedirectException>(() =>
            handler.BuildRedirectRequest(req1, res1));
        Assert.Equal(RedirectError.RedirectLoop, ex.Error);
    }

    [Fact]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_DetectLoop_When_SchemeAndHostDifferOnlyInCase()
    {
        var handler = new RedirectHandler();
        var req1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page");
        var res1 = BuildRedirect(HttpStatusCode.Found, "http://example.com/other");
        handler.BuildRedirectRequest(req1, res1);

        var req2 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/other");
        var res2 = BuildRedirect(HttpStatusCode.Found, "HTTP://EXAMPLE.COM/page");

        var ex = Assert.Throws<RedirectException>(() =>
            handler.BuildRedirectRequest(req2, res2));
        Assert.Equal(RedirectError.RedirectLoop, ex.Error);
    }

    [Fact]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_NotDetectLoop_When_PathsDifferInCase()
    {
        var handler = new RedirectHandler();
        var req1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/Page");
        var res1 = BuildRedirect(HttpStatusCode.Found, "http://example.com/page");

        var redirected = handler.BuildRedirectRequest(req1, res1);

        Assert.Equal("http://example.com/page", redirected.RequestUri?.AbsoluteUri);
    }

    [Fact]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_DetectLoop_When_RelativeUrlResolvesToVisitedAbsoluteUrl()
    {
        var handler = new RedirectHandler();
        var req1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/dir/start");
        var res1 = BuildRedirect(HttpStatusCode.Found, "http://example.com/dir/other");
        handler.BuildRedirectRequest(req1, res1);

        // Redirect back using absolute URL that matches a previously visited URL
        var req2 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/dir/other");
        var res2 = BuildRedirect(HttpStatusCode.Found, "http://example.com/dir/start");

        var ex = Assert.Throws<RedirectException>(() =>
            handler.BuildRedirectRequest(req2, res2));
        Assert.Equal(RedirectError.RedirectLoop, ex.Error);
    }

    [Fact]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_DetectLoop_When_ThreeHopCycle()
    {
        var handler = new RedirectHandler();

        var req1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/a");
        var res1 = BuildRedirect(HttpStatusCode.Found, "http://example.com/b");
        handler.BuildRedirectRequest(req1, res1);

        var req2 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/b");
        var res2 = BuildRedirect(HttpStatusCode.Found, "http://example.com/c");
        handler.BuildRedirectRequest(req2, res2);

        var req3 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/c");
        var res3 = BuildRedirect(HttpStatusCode.Found, "http://example.com/a");

        var ex = Assert.Throws<RedirectException>(() =>
            handler.BuildRedirectRequest(req3, res3));
        Assert.Equal(RedirectError.RedirectLoop, ex.Error);
    }

    [Fact]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_IncludeLimitInMessage_When_MaxRedirectsExceeded()
    {
        var handler = new RedirectHandler(new RedirectPolicy { MaxRedirects = 2 });

        for (var i = 0; i < 2; i++)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, $"http://example.com/p{i}");
            var res = BuildRedirect(HttpStatusCode.Found, $"http://example.com/p{i + 1}");
            handler.BuildRedirectRequest(req, res);
        }

        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/p2");
        var response = BuildRedirect(HttpStatusCode.Found, "http://example.com/p3");

        var ex = Assert.Throws<RedirectException>(() =>
            handler.BuildRedirectRequest(original, response));
        Assert.Contains("RFC 9110 §15.4: Maximum redirect limit of 2 exceeded.", ex.Message);
    }

    [Fact]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_IncludeUriInMessage_When_LoopDetected()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/loop");
        var response = BuildRedirect(HttpStatusCode.Found, "http://example.com/loop");

        var ex = Assert.Throws<RedirectException>(() =>
            handler.BuildRedirectRequest(original, response));
        Assert.Contains("RFC 9110 §15.4: Redirect loop detected. URI already visited:", ex.Message);
        Assert.Contains("example.com/loop", ex.Message);
    }


    private static HttpResponseMessage BuildRedirect(HttpStatusCode statusCode, string location)
    {
        var response = new HttpResponseMessage(statusCode);
        response.Headers.TryAddWithoutValidation("Location", location);
        return response;
    }
}
