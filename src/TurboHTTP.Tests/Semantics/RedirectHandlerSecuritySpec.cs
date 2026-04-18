using System.Net;
using TurboHTTP.Protocol.Cookies;
using TurboHTTP.Protocol.Semantics;

namespace TurboHTTP.Tests.Semantics;

public sealed class RedirectHandlerSecuritySpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_StripAuthorizationHeader_When_CrossOriginRedirect()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/api");
        original.Headers.TryAddWithoutValidation("Authorization", "Bearer secret-token");
        original.Headers.TryAddWithoutValidation("X-Custom-Header", "custom-value");
        var response = BuildRedirect(HttpStatusCode.Found, "http://other.com/api");

        var redirected = handler.BuildRedirectRequest(original, response);

        Assert.False(redirected.Headers.Contains("Authorization"),
            "Authorization header must NOT be forwarded to a different origin");
        Assert.True(redirected.Headers.Contains("X-Custom-Header"),
            "Non-sensitive headers should still be forwarded");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_PreserveAuthorizationHeader_When_SameOriginRedirect()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/old");
        original.Headers.TryAddWithoutValidation("Authorization", "Bearer secret-token");
        var response = BuildRedirect(HttpStatusCode.Found, "http://example.com/new");

        var redirected = handler.BuildRedirectRequest(original, response);

        Assert.True(redirected.Headers.Contains("Authorization"),
            "Authorization header should be preserved for same-origin redirects");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_StripAuthorizationHeader_When_SchemeChanges()
    {
        var handler = new RedirectHandler(new RedirectPolicy { AllowHttpsToHttpDowngrade = true });
        var original = new HttpRequestMessage(HttpMethod.Get, "https://example.com/api");
        original.Headers.TryAddWithoutValidation("Authorization", "Bearer token");
        var response = BuildRedirect(HttpStatusCode.Found, "http://example.com/api");

        var redirected = handler.BuildRedirectRequest(original, response);

        Assert.False(redirected.Headers.Contains("Authorization"),
            "Authorization must be stripped when scheme changes");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_StripAuthorizationHeader_When_PortChanges()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com:8080/api");
        original.Headers.TryAddWithoutValidation("Authorization", "Bearer token");
        var response = BuildRedirect(HttpStatusCode.Found, "http://example.com:9090/api");

        var redirected = handler.BuildRedirectRequest(original, response);

        Assert.False(redirected.Headers.Contains("Authorization"),
            "Authorization must be stripped when port changes");
    }


    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_ThrowRedirectException_ProtocolDowngrade_When_HttpsToHttpDowngrade()
    {
        var handler = new RedirectHandler(); // AllowHttpsToHttpDowngrade = false by default
        var original = new HttpRequestMessage(HttpMethod.Get, "https://example.com/secure");
        var response = BuildRedirect(HttpStatusCode.Found, "http://example.com/insecure");

        var ex = Assert.Throws<RedirectException>(() =>
            handler.BuildRedirectRequest(original, response));
        Assert.Equal(RedirectError.ProtocolDowngrade, ex.Error);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_AllowDowngrade_When_PolicyPermitsDowngrade()
    {
        var handler = new RedirectHandler(new RedirectPolicy { AllowHttpsToHttpDowngrade = true });
        var original = new HttpRequestMessage(HttpMethod.Get, "https://example.com/secure");
        var response = BuildRedirect(HttpStatusCode.Found, "http://example.com/insecure");

        var redirected = handler.BuildRedirectRequest(original, response);

        Assert.Equal("http://example.com/insecure", redirected.RequestUri?.AbsoluteUri);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_AllowUpgrade_When_HttpToHttpsRedirect()
    {
        var handler = new RedirectHandler(); // only blocks downgrade, not upgrade
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page");
        var response = BuildRedirect(HttpStatusCode.Found, "https://example.com/page");

        var redirected = handler.BuildRedirectRequest(original, response);

        Assert.Equal("https://example.com/page", redirected.RequestUri?.AbsoluteUri);
    }


    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_ClearRedirectCountAndHistory_When_Reset()
    {
        var handler = new RedirectHandler(new RedirectPolicy { MaxRedirects = 2 });
        var req1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/a");
        var res1 = BuildRedirect(HttpStatusCode.Found, "http://example.com/b");
        handler.BuildRedirectRequest(req1, res1);

        handler.Reset();

        Assert.Equal(0, handler.RedirectCount);

        // Should be able to follow redirects again from scratch
        var req2 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/a");
        var res2 = BuildRedirect(HttpStatusCode.Found, "http://example.com/b");
        var redirected = handler.BuildRedirectRequest(req2, res2);
        Assert.NotNull(redirected);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_AllowPreviouslyVisitedUri_When_AfterReset()
    {
        var handler = new RedirectHandler();
        var req1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/a");
        var res1 = BuildRedirect(HttpStatusCode.Found, "http://example.com/b");
        handler.BuildRedirectRequest(req1, res1);

        handler.Reset();

        // /a should be allowed again after reset
        var req2 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/b");
        var res2 = BuildRedirect(HttpStatusCode.Found, "http://example.com/a");
        var redirected = handler.BuildRedirectRequest(req2, res2);
        Assert.NotNull(redirected);
    }


    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_CopyNonSensitiveHeaders_When_Redirecting()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page");
        original.Headers.TryAddWithoutValidation("Accept", "application/json");
        original.Headers.TryAddWithoutValidation("Accept-Language", "en-US");
        original.Headers.TryAddWithoutValidation("X-Request-Id", "abc-123");
        var response = BuildRedirect(HttpStatusCode.Found, "http://example.com/new-page");

        var redirected = handler.BuildRedirectRequest(original, response);

        Assert.True(redirected.Headers.Contains("Accept"));
        Assert.True(redirected.Headers.Contains("Accept-Language"));
        Assert.True(redirected.Headers.Contains("X-Request-Id"));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_NotCopyHostHeader_When_Redirecting()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page");
        original.Headers.TryAddWithoutValidation("Host", "example.com");
        var response = BuildRedirect(HttpStatusCode.Found, "http://other.com/page");

        var redirected = handler.BuildRedirectRequest(original, response);

        Assert.False(redirected.Headers.Contains("Host"),
            "Host header must not be blindly copied — it is set from the new URI");
    }


    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_HaveMaxRedirects10_When_UsingDefaultPolicy()
    {
        Assert.Equal(10, RedirectPolicy.Default.MaxRedirects);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_NotAllowDowngrade_When_UsingDefaultPolicy()
    {
        Assert.False(RedirectPolicy.Default.AllowHttpsToHttpDowngrade);
    }


    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_ThrowArgumentNullException_When_IsRedirectWithNullResponse()
    {
        Assert.Throws<ArgumentNullException>(() =>
            RedirectHandler.IsRedirect(null!));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_ThrowArgumentNullException_When_OriginalRequestIsNull()
    {
        var handler = new RedirectHandler();
        var response = BuildRedirect(HttpStatusCode.Found, "http://example.com/b");

        Assert.Throws<ArgumentNullException>(() =>
            handler.BuildRedirectRequest(null!, response));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_ThrowArgumentNullException_When_ResponseIsNull()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/a");

        Assert.Throws<ArgumentNullException>(() =>
            handler.BuildRedirectRequest(original, null!));
    }


    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_StripCookieHeader_When_Redirecting()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page");
        original.Headers.TryAddWithoutValidation("Cookie", "session=abc123");
        var response = BuildRedirect(HttpStatusCode.Found, "http://example.com/new");

        var redirected = handler.BuildRedirectRequest(original, response);

        Assert.False(redirected.Headers.Contains("Cookie"),
            "Cookie header must not be blindly forwarded — it must be re-evaluated per redirect URI");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_ReapplyCookies_When_SameOriginRedirectWithJar()
    {
        var handler = new RedirectHandler();
        var jar = new CookieJar();
        // Pre-populate jar with a matching cookie for example.com
        var setResponse = new HttpResponseMessage(HttpStatusCode.OK);
        setResponse.Headers.TryAddWithoutValidation("Set-Cookie", "session=abc123; Path=/");
        jar.ProcessResponse(new Uri("http://example.com/"), setResponse);

        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page");
        var response = BuildRedirect(HttpStatusCode.Found, "http://example.com/new");

        var redirected = handler.BuildRedirectRequest(original, response, jar);

        Assert.True(redirected.Headers.Contains("Cookie"),
            "Cookies applicable to the redirect URI should be re-applied");
        var cookieHeader = string.Join("; ", redirected.Headers.GetValues("Cookie"));
        Assert.Contains("session=abc123", cookieHeader);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_NotReapplyCookies_When_CrossOriginRedirectWithJar()
    {
        var handler = new RedirectHandler();
        var jar = new CookieJar();
        // Cookie for example.com
        var setResponse = new HttpResponseMessage(HttpStatusCode.OK);
        setResponse.Headers.TryAddWithoutValidation("Set-Cookie", "session=abc123; Path=/");
        jar.ProcessResponse(new Uri("http://example.com/"), setResponse);

        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page");
        var response = BuildRedirect(HttpStatusCode.Found, "http://other.com/page");

        var redirected = handler.BuildRedirectRequest(original, response, jar);

        Assert.False(redirected.Headers.Contains("Cookie"),
            "Cookies set for example.com must not be forwarded to other.com");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_ProcessSetCookieFromRedirectResponse_When_RedirectingWithJar()
    {
        var handler = new RedirectHandler();
        var jar = new CookieJar();

        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/login");
        // Redirect response sets a cookie
        var response = BuildRedirect(HttpStatusCode.Found, "http://example.com/dashboard");
        response.Headers.TryAddWithoutValidation("Set-Cookie", "auth=token123; Path=/");

        handler.BuildRedirectRequest(original, response, jar);

        // Cookie should now be in the jar
        Assert.Equal(1, jar.Count);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_ApplySetCookieToRedirectRequest_When_RedirectingWithJar()
    {
        var handler = new RedirectHandler();
        var jar = new CookieJar();

        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/login");
        // The redirect response both redirects AND sets a cookie
        var response = BuildRedirect(HttpStatusCode.Found, "http://example.com/dashboard");
        response.Headers.TryAddWithoutValidation("Set-Cookie", "auth=token123; Path=/");

        var redirected = handler.BuildRedirectRequest(original, response, jar);

        Assert.True(redirected.Headers.Contains("Cookie"),
            "Cookie set by redirect response should be applied to the redirect request");
        var cookieHeader = string.Join("; ", redirected.Headers.GetValues("Cookie"));
        Assert.Contains("auth=token123", cookieHeader);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_SendSecureCookiesOnlyToHttps_When_RedirectingWithJar()
    {
        var jar = new CookieJar();
        // Pre-populate with a Secure cookie
        var setResponse = new HttpResponseMessage(HttpStatusCode.OK);
        setResponse.Headers.TryAddWithoutValidation("Set-Cookie", "secret=val; Path=/; Secure");
        jar.ProcessResponse(new Uri("https://example.com/"), setResponse);

        var original = new HttpRequestMessage(HttpMethod.Get, "https://example.com/page");

        // Redirect to HTTP (downgrade allowed for testing purposes)
        var policyAllowDowngrade = new RedirectPolicy { AllowHttpsToHttpDowngrade = true };
        var handlerAllowDowngrade = new RedirectHandler(policyAllowDowngrade);
        var response = BuildRedirect(HttpStatusCode.Found, "http://example.com/new");

        var redirected = handlerAllowDowngrade.BuildRedirectRequest(original, response, jar);

        Assert.False(redirected.Headers.Contains("Cookie"),
            "Secure cookies must not be sent over HTTP");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_SendSecureCookies_When_RedirectStaysOnHttpsWithJar()
    {
        var handler = new RedirectHandler();
        var jar = new CookieJar();
        // Pre-populate with a Secure cookie
        var setResponse = new HttpResponseMessage(HttpStatusCode.OK);
        setResponse.Headers.TryAddWithoutValidation("Set-Cookie", "secret=val; Path=/; Secure");
        jar.ProcessResponse(new Uri("https://example.com/"), setResponse);

        var original = new HttpRequestMessage(HttpMethod.Get, "https://example.com/page");
        var response = BuildRedirect(HttpStatusCode.Found, "https://example.com/new");

        var redirected = handler.BuildRedirectRequest(original, response, jar);

        Assert.True(redirected.Headers.Contains("Cookie"),
            "Secure cookies should be sent when redirect stays on HTTPS");
        var cookieHeader = string.Join("; ", redirected.Headers.GetValues("Cookie"));
        Assert.Contains("secret=val", cookieHeader);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_NotSendPathRestrictedCookie_When_PathDoesNotMatchWithJar()
    {
        var handler = new RedirectHandler();
        var jar = new CookieJar();
        // Cookie is only for /admin path
        var setResponse = new HttpResponseMessage(HttpStatusCode.OK);
        setResponse.Headers.TryAddWithoutValidation("Set-Cookie", "admin=secret; Path=/admin");
        jar.ProcessResponse(new Uri("http://example.com/admin"), setResponse);

        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page");
        var response = BuildRedirect(HttpStatusCode.Found, "http://example.com/public");

        var redirected = handler.BuildRedirectRequest(original, response, jar);

        Assert.False(redirected.Headers.Contains("Cookie"),
            "Cookie with path=/admin must not be sent to /public");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_SendPathRestrictedCookie_When_PathMatchesWithJar()
    {
        var handler = new RedirectHandler();
        var jar = new CookieJar();
        // Cookie for /admin path
        var setResponse = new HttpResponseMessage(HttpStatusCode.OK);
        setResponse.Headers.TryAddWithoutValidation("Set-Cookie", "admin=secret; Path=/admin");
        jar.ProcessResponse(new Uri("http://example.com/admin"), setResponse);

        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page");
        var response = BuildRedirect(HttpStatusCode.Found, "http://example.com/admin/dashboard");

        var redirected = handler.BuildRedirectRequest(original, response, jar);

        Assert.True(redirected.Headers.Contains("Cookie"),
            "Cookie with path=/admin should be sent to /admin/dashboard");
        var cookieHeader = string.Join("; ", redirected.Headers.GetValues("Cookie"));
        Assert.Contains("admin=secret", cookieHeader);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_ThrowArgumentNullException_When_CookieJarIsNull()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/a");
        var response = BuildRedirect(HttpStatusCode.Found, "http://example.com/b");

        Assert.Throws<ArgumentNullException>(() =>
            handler.BuildRedirectRequest(original, response, null!));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_NotAddCookieHeader_When_JarIsEmpty()
    {
        var handler = new RedirectHandler();
        var jar = new CookieJar();

        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page");
        var response = BuildRedirect(HttpStatusCode.Found, "http://example.com/new");

        var redirected = handler.BuildRedirectRequest(original, response, jar);

        Assert.False(redirected.Headers.Contains("Cookie"),
            "Empty jar should result in no Cookie header");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.4")]
    public void Should_ReapplyDomainCookie_When_SubdomainRedirectWithJar()
    {
        var handler = new RedirectHandler();
        var jar = new CookieJar();
        // Domain cookie (applies to all subdomains of example.com)
        var setResponse = new HttpResponseMessage(HttpStatusCode.OK);
        setResponse.Headers.TryAddWithoutValidation("Set-Cookie", "track=xyz; Domain=example.com; Path=/");
        jar.ProcessResponse(new Uri("http://example.com/"), setResponse);

        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page");
        var response = BuildRedirect(HttpStatusCode.Found, "http://sub.example.com/page");

        var redirected = handler.BuildRedirectRequest(original, response, jar);

        Assert.True(redirected.Headers.Contains("Cookie"),
            "Domain cookie should be re-applied to subdomain redirect");
        var cookieHeader = string.Join("; ", redirected.Headers.GetValues("Cookie"));
        Assert.Contains("track=xyz", cookieHeader);
    }


    private static HttpResponseMessage BuildRedirect(HttpStatusCode statusCode, string location)
    {
        var response = new HttpResponseMessage(statusCode);
        response.Headers.TryAddWithoutValidation("Location", location);
        return response;
    }
}