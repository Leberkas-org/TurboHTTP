using System.Net;
using TurboHttp.Protocol.RFC6265;
using TurboHttp.Protocol.RFC9110;

namespace TurboHttp.Tests.RFC9110;

/// <summary>
/// RFC 9110 §15.4 — Redirect handling tests.
/// Covers all redirect status codes, method rewriting, body preservation,
/// loop detection, max redirect enforcement, cross-origin security, and
/// HTTPS-to-HTTP downgrade protection.
/// </summary>
/// <remarks>
/// Class under test: <see cref="RedirectHandler"/>.
/// RFC 9110 §15.4: Redirect responses (301/302/303/307/308) require method rewriting and loop detection.
/// </remarks>
public sealed class RedirectHandlerTests
{

    [Theory(DisplayName = "RFC9110-15.4-RH-001: IsRedirect returns true for redirect status codes")]
    [InlineData(301)]
    [InlineData(302)]
    [InlineData(303)]
    [InlineData(307)]
    [InlineData(308)]
    public void Should_ReturnTrue_When_StatusCodeIsRedirect(int statusCode)
    {
        var response = new HttpResponseMessage((HttpStatusCode)statusCode);
        Assert.True(RedirectHandler.IsRedirect(response));
    }

    [Theory(DisplayName = "RFC9110-15.4-RH-002: IsRedirect returns false for non-redirect status codes")]
    [InlineData(200)]
    [InlineData(201)]
    [InlineData(304)]
    [InlineData(400)]
    [InlineData(404)]
    [InlineData(500)]
    public void Should_ReturnFalse_When_StatusCodeIsNotRedirect(int statusCode)
    {
        var response = new HttpResponseMessage((HttpStatusCode)statusCode);
        Assert.False(RedirectHandler.IsRedirect(response));
    }


    [Fact(DisplayName = "RFC9110-15.4-RH-003: 303 rewrites POST to GET and drops body")]
    public void Should_RewritePostToGet_When_303SeeOther()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Post, "http://example.com/resource")
        {
            Content = new StringContent("body data")
        };
        var response = BuildRedirect(HttpStatusCode.SeeOther, "http://example.com/new-location");

        var redirected = handler.BuildRedirectRequest(original, response);

        Assert.Equal(HttpMethod.Get, redirected.Method);
        Assert.Null(redirected.Content);
    }

    [Fact(DisplayName = "RFC9110-15.4-RH-004: 303 rewrites PUT to GET and drops body")]
    public void Should_RewritePutToGet_When_303SeeOther()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Put, "http://example.com/resource")
        {
            Content = new StringContent("body data")
        };
        var response = BuildRedirect(HttpStatusCode.SeeOther, "http://example.com/new");

        var redirected = handler.BuildRedirectRequest(original, response);

        Assert.Equal(HttpMethod.Get, redirected.Method);
        Assert.Null(redirected.Content);
    }

    [Fact(DisplayName = "RFC9110-15.4-RH-005: 303 rewrites DELETE to GET")]
    public void Should_RewriteDeleteToGet_When_303SeeOther()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Delete, "http://example.com/resource");
        var response = BuildRedirect(HttpStatusCode.SeeOther, "http://example.com/new");

        var redirected = handler.BuildRedirectRequest(original, response);

        Assert.Equal(HttpMethod.Get, redirected.Method);
    }


    [Fact(DisplayName = "RFC9110-15.4-RH-006: 307 preserves POST method and body")]
    public void Should_PreservePostMethodAndBody_When_307TemporaryRedirect()
    {
        var handler = new RedirectHandler();
        var content = new StringContent("request body");
        var original = new HttpRequestMessage(HttpMethod.Post, "http://example.com/resource")
        {
            Content = content
        };
        var response = BuildRedirect(HttpStatusCode.TemporaryRedirect, "http://example.com/new");

        var redirected = handler.BuildRedirectRequest(original, response);

        Assert.Equal(HttpMethod.Post, redirected.Method);
        Assert.Same(content, redirected.Content);
    }

    [Fact(DisplayName = "RFC9110-15.4-RH-007: 307 preserves PUT method and body")]
    public void Should_PreservePutMethodAndBody_When_307TemporaryRedirect()
    {
        var handler = new RedirectHandler();
        var content = new StringContent("request body");
        var original = new HttpRequestMessage(HttpMethod.Put, "http://example.com/resource")
        {
            Content = content
        };
        var response = BuildRedirect(HttpStatusCode.TemporaryRedirect, "http://example.com/new");

        var redirected = handler.BuildRedirectRequest(original, response);

        Assert.Equal(HttpMethod.Put, redirected.Method);
        Assert.Same(content, redirected.Content);
    }

    [Fact(DisplayName = "RFC9110-15.4-RH-008: 307 preserves DELETE method")]
    public void Should_PreserveDeleteMethod_When_307TemporaryRedirect()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Delete, "http://example.com/resource");
        var response = BuildRedirect(HttpStatusCode.TemporaryRedirect, "http://example.com/new");

        var redirected = handler.BuildRedirectRequest(original, response);

        Assert.Equal(HttpMethod.Delete, redirected.Method);
    }


    [Fact(DisplayName = "RFC9110-15.4-RH-009: 308 preserves POST method and body")]
    public void Should_PreservePostMethodAndBody_When_308PermanentRedirect()
    {
        var handler = new RedirectHandler();
        var content = new StringContent("body");
        var original = new HttpRequestMessage(HttpMethod.Post, "http://example.com/resource")
        {
            Content = content
        };
        var response = BuildRedirect(HttpStatusCode.PermanentRedirect, "http://example.com/new");

        var redirected = handler.BuildRedirectRequest(original, response);

        Assert.Equal(HttpMethod.Post, redirected.Method);
        Assert.Same(content, redirected.Content);
    }

    [Fact(DisplayName = "RFC9110-15.4-RH-010: 308 preserves PATCH method and body")]
    public void Should_PreservePatchMethodAndBody_When_308PermanentRedirect()
    {
        var handler = new RedirectHandler();
        var content = new StringContent("patch body");
        var original = new HttpRequestMessage(HttpMethod.Patch, "http://example.com/resource")
        {
            Content = content
        };
        var response = BuildRedirect(HttpStatusCode.PermanentRedirect, "http://example.com/new");

        var redirected = handler.BuildRedirectRequest(original, response);

        Assert.Equal(HttpMethod.Patch, redirected.Method);
        Assert.Same(content, redirected.Content);
    }


    [Theory(DisplayName = "RFC9110-15.4-RH-011: 301/302 rewrites POST to GET (historical behavior)")]
    [InlineData(301)]
    [InlineData(302)]
    public void Should_RewritePostToGet_When_301Or302Redirect(int statusCode)
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Post, "http://example.com/resource")
        {
            Content = new StringContent("body")
        };
        var response = BuildRedirect((HttpStatusCode)statusCode, "http://example.com/new");

        var redirected = handler.BuildRedirectRequest(original, response);

        Assert.Equal(HttpMethod.Get, redirected.Method);
        Assert.Null(redirected.Content);
    }

    [Theory(DisplayName = "RFC9110-15.4-RH-012: 301/302 preserves GET method")]
    [InlineData(301)]
    [InlineData(302)]
    public void Should_PreserveGetMethod_When_301Or302Redirect(int statusCode)
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        var response = BuildRedirect((HttpStatusCode)statusCode, "http://example.com/new");

        var redirected = handler.BuildRedirectRequest(original, response);

        Assert.Equal(HttpMethod.Get, redirected.Method);
    }

    [Theory(DisplayName = "RFC9110-15.4-RH-013: 301/302 preserves HEAD method")]
    [InlineData(301)]
    [InlineData(302)]
    public void Should_PreserveHeadMethod_When_301Or302Redirect(int statusCode)
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Head, "http://example.com/resource");
        var response = BuildRedirect((HttpStatusCode)statusCode, "http://example.com/new");

        var redirected = handler.BuildRedirectRequest(original, response);

        Assert.Equal(HttpMethod.Head, redirected.Method);
    }


    [Fact(DisplayName = "RFC9110-15.4-RH-014: Absolute Location URI used as-is")]
    public void Should_UseAbsoluteLocation_When_LocationIsAbsolute()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page");
        var response = BuildRedirect(HttpStatusCode.MovedPermanently, "http://other.com/new-page");

        var redirected = handler.BuildRedirectRequest(original, response);

        Assert.Equal("http://other.com/new-page", redirected.RequestUri?.AbsoluteUri);
    }

    [Fact(DisplayName = "RFC9110-15.4-RH-015: Relative Location URI resolved against request URI")]
    public void Should_ResolveRelativeLocation_When_LocationIsRelative()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/api/v1/resource");
        var response = BuildRedirect(HttpStatusCode.MovedPermanently, "/api/v2/resource");

        var redirected = handler.BuildRedirectRequest(original, response);

        Assert.Equal("http://example.com/api/v2/resource", redirected.RequestUri?.AbsoluteUri);
    }

    [Fact(DisplayName = "RFC9110-15.4-RH-016: Relative path Location URI resolved correctly")]
    public void Should_ResolveRelativePath_When_LocationIsRelativePath()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/dir/page");
        var response = BuildRedirect(HttpStatusCode.Found, "other-page");

        var redirected = handler.BuildRedirectRequest(original, response);

        Assert.NotNull(redirected.RequestUri);
        Assert.Equal("example.com", redirected.RequestUri.Host);
    }


    [Fact(DisplayName = "RFC9110-15.4-RH-017: Throws RedirectException when max redirects exceeded")]
    public void Should_ThrowRedirectException_When_MaxRedirectsExceeded()
    {
        var handler = new RedirectHandler(new RedirectPolicy { MaxRedirects = 3 });

        for (var i = 0; i < 3; i++)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, $"http://example.com/page{i}");
            var res = BuildRedirect(HttpStatusCode.MovedPermanently, $"http://example.com/page{i + 1}");
            handler.BuildRedirectRequest(req, res);
        }

        // 4th redirect should throw
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page3");
        var response = BuildRedirect(HttpStatusCode.MovedPermanently, "http://example.com/page4");

        var ex = Assert.Throws<RedirectException>(() =>
            handler.BuildRedirectRequest(original, response));

        Assert.Equal(RedirectError.MaxRedirectsExceeded, ex.Error);
    }

    [Fact(DisplayName = "RFC9110-15.4-RH-018: Throws RedirectException after default max 10 redirects")]
    public void Should_ThrowRedirectException_When_DefaultMaxRedirectsExceeded()
    {
        var handler = new RedirectHandler(); // default: 10

        for (var i = 0; i < 10; i++)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, $"http://example.com/page{i}");
            var res = BuildRedirect(HttpStatusCode.Found, $"http://example.com/page{i + 1}");
            handler.BuildRedirectRequest(req, res);
        }

        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page10");
        var response = BuildRedirect(HttpStatusCode.Found, "http://example.com/page11");

        Assert.Throws<RedirectException>(() =>
            handler.BuildRedirectRequest(original, response));
    }

    [Fact(DisplayName = "RFC9110-15.4-RH-019: RedirectCount tracks number of redirects")]
    public void Should_TrackRedirectCount_When_RedirectsFollow()
    {
        var handler = new RedirectHandler();
        Assert.Equal(0, handler.RedirectCount);

        var req1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/a");
        var res1 = BuildRedirect(HttpStatusCode.Found, "http://example.com/b");
        handler.BuildRedirectRequest(req1, res1);
        Assert.Equal(1, handler.RedirectCount);

        var req2 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/b");
        var res2 = BuildRedirect(HttpStatusCode.Found, "http://example.com/c");
        handler.BuildRedirectRequest(req2, res2);
        Assert.Equal(2, handler.RedirectCount);
    }


    [Fact(DisplayName = "RFC9110-15.4-RH-020: Throws RedirectException on direct redirect loop")]
    public void Should_ThrowRedirectException_When_DirectRedirectLoop()
    {
        var handler = new RedirectHandler();
        var req1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/a");
        var res1 = BuildRedirect(HttpStatusCode.Found, "http://example.com/b");
        handler.BuildRedirectRequest(req1, res1);

        // Now try to redirect back to /a — loop detected
        var req2 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/b");
        var res2 = BuildRedirect(HttpStatusCode.Found, "http://example.com/a");

        var ex = Assert.Throws<RedirectException>(() =>
            handler.BuildRedirectRequest(req2, res2));

        Assert.Equal(RedirectError.RedirectLoop, ex.Error);
    }

    [Fact(DisplayName = "RFC9110-15.4-RH-021: Throws RedirectException on self-redirect (A → A)")]
    public void Should_ThrowRedirectException_When_SelfRedirect()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page");
        var response = BuildRedirect(HttpStatusCode.Found, "http://example.com/page");

        var ex = Assert.Throws<RedirectException>(() =>
            handler.BuildRedirectRequest(original, response));

        Assert.Equal(RedirectError.RedirectLoop, ex.Error);
    }


    [Fact(DisplayName = "RFC9110-15.4-RH-022: Throws RedirectException when Location header is missing")]
    public void Should_ThrowRedirectException_When_LocationHeaderMissing()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page");
        var response = new HttpResponseMessage(HttpStatusCode.MovedPermanently);

        var ex = Assert.Throws<RedirectException>(() =>
            handler.BuildRedirectRequest(original, response));

        Assert.Equal(RedirectError.MissingLocationHeader, ex.Error);
    }

    [Fact(DisplayName = "RFC9110-15.4-RH-023: 308 preserves GET method (no body rewrite)")]
    public void Should_PreserveGetMethod_When_308PermanentRedirect()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        var response = BuildRedirect(HttpStatusCode.PermanentRedirect, "http://example.com/new");

        var redirected = handler.BuildRedirectRequest(original, response);

        Assert.Equal(HttpMethod.Get, redirected.Method);
        Assert.Null(redirected.Content);
    }


    [Fact(DisplayName = "RFC9110-15.4-RH-024: Strips Authorization header on cross-origin redirect")]
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

    [Fact(DisplayName = "RFC9110-15.4-RH-025: Preserves Authorization header on same-origin redirect")]
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

    [Fact(DisplayName = "RFC9110-15.4-RH-026: Strips Authorization header when scheme changes (HTTPS→HTTP)")]
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

    [Fact(DisplayName = "RFC9110-15.4-RH-027: Strips Authorization header when port changes")]
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


    [Fact(DisplayName = "RFC9110-15.4-RH-028: Throws RedirectException with ProtocolDowngrade on HTTPS to HTTP redirect")]
    public void Should_ThrowRedirectException_ProtocolDowngrade_When_HttpsToHttpDowngrade()
    {
        var handler = new RedirectHandler(); // AllowHttpsToHttpDowngrade = false by default
        var original = new HttpRequestMessage(HttpMethod.Get, "https://example.com/secure");
        var response = BuildRedirect(HttpStatusCode.Found, "http://example.com/insecure");

        var ex = Assert.Throws<RedirectException>(() =>
            handler.BuildRedirectRequest(original, response));
        Assert.Equal(RedirectError.ProtocolDowngrade, ex.Error);
    }

    [Fact(DisplayName = "RFC9110-15.4-RH-029: Allows HTTPS to HTTP downgrade when policy permits")]
    public void Should_AllowDowngrade_When_PolicyPermitsDowngrade()
    {
        var handler = new RedirectHandler(new RedirectPolicy { AllowHttpsToHttpDowngrade = true });
        var original = new HttpRequestMessage(HttpMethod.Get, "https://example.com/secure");
        var response = BuildRedirect(HttpStatusCode.Found, "http://example.com/insecure");

        var redirected = handler.BuildRedirectRequest(original, response);

        Assert.Equal("http://example.com/insecure", redirected.RequestUri?.AbsoluteUri);
    }

    [Fact(DisplayName = "RFC9110-15.4-RH-030: Allows HTTP to HTTPS upgrade (no downgrade block)")]
    public void Should_AllowUpgrade_When_HttpToHttpsRedirect()
    {
        var handler = new RedirectHandler(); // only blocks downgrade, not upgrade
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page");
        var response = BuildRedirect(HttpStatusCode.Found, "https://example.com/page");

        var redirected = handler.BuildRedirectRequest(original, response);

        Assert.Equal("https://example.com/page", redirected.RequestUri?.AbsoluteUri);
    }


    [Fact(DisplayName = "RFC9110-15.4-RH-031: Reset clears redirect count and history")]
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

    [Fact(DisplayName = "RFC9110-15.4-RH-032: Reset allows previously visited URI to be visited again")]
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


    [Fact(DisplayName = "RFC9110-15.4-RH-033: Non-sensitive headers are copied on redirect")]
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

    [Fact(DisplayName = "RFC9110-15.4-RH-034: Host header is not blindly copied on redirect")]
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


    [Fact(DisplayName = "RFC9110-15.4-RH-035: Default policy has MaxRedirects = 10")]
    public void Should_HaveMaxRedirects10_When_UsingDefaultPolicy()
    {
        Assert.Equal(10, RedirectPolicy.Default.MaxRedirects);
    }

    [Fact(DisplayName = "RFC9110-15.4-RH-036: Default policy does not allow HTTPS to HTTP downgrade")]
    public void Should_NotAllowDowngrade_When_UsingDefaultPolicy()
    {
        Assert.False(RedirectPolicy.Default.AllowHttpsToHttpDowngrade);
    }


    [Fact(DisplayName = "RFC9110-15.4-RH-037: IsRedirect throws ArgumentNullException for null response")]
    public void Should_ThrowArgumentNullException_When_IsRedirectWithNullResponse()
    {
        Assert.Throws<ArgumentNullException>(() =>
            RedirectHandler.IsRedirect(null!));
    }

    [Fact(DisplayName = "RFC9110-15.4-RH-038: BuildRedirectRequest throws ArgumentNullException for null original")]
    public void Should_ThrowArgumentNullException_When_OriginalRequestIsNull()
    {
        var handler = new RedirectHandler();
        var response = BuildRedirect(HttpStatusCode.Found, "http://example.com/b");

        Assert.Throws<ArgumentNullException>(() =>
            handler.BuildRedirectRequest(null!, response));
    }

    [Fact(DisplayName = "RFC9110-15.4-RH-039: BuildRedirectRequest throws ArgumentNullException for null response")]
    public void Should_ThrowArgumentNullException_When_ResponseIsNull()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/a");

        Assert.Throws<ArgumentNullException>(() =>
            handler.BuildRedirectRequest(original, null!));
    }


    [Fact(DisplayName = "RFC9110-15.4-RH-040: Cookie header is stripped when building redirect request")]
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

    [Fact(DisplayName = "RFC9110-15.4-RH-041: With CookieJar, cookies re-applied for same-origin redirect")]
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

    [Fact(DisplayName = "RFC9110-15.4-RH-042: With CookieJar, cookies NOT re-applied for different domain")]
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

    [Fact(DisplayName = "RFC9110-15.4-RH-043: With CookieJar, Set-Cookie from redirect response is processed")]
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

    [Fact(DisplayName = "RFC9110-15.4-RH-044: With CookieJar, Set-Cookie from redirect applied to next hop")]
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

    [Fact(DisplayName = "RFC9110-15.4-RH-045: With CookieJar, Secure cookies only sent to HTTPS redirect")]
    public void Should_SendSecureCookiesOnlyToHttps_When_RedirectingWithJar()
    {
        var handler = new RedirectHandler();
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

    [Fact(DisplayName = "RFC9110-15.4-RH-046: With CookieJar, Secure cookies sent when redirect stays on HTTPS")]
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

    [Fact(DisplayName = "RFC9110-15.4-RH-047: With CookieJar, path-restricted cookie not sent for non-matching path")]
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

    [Fact(DisplayName = "RFC9110-15.4-RH-048: With CookieJar, path-restricted cookie sent for matching path")]
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

    [Fact(DisplayName = "RFC9110-15.4-RH-049: BuildRedirectRequest(jar) throws ArgumentNullException for null jar")]
    public void Should_ThrowArgumentNullException_When_CookieJarIsNull()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/a");
        var response = BuildRedirect(HttpStatusCode.Found, "http://example.com/b");

        Assert.Throws<ArgumentNullException>(() =>
            handler.BuildRedirectRequest(original, response, null!));
    }

    [Fact(DisplayName = "RFC9110-15.4-RH-050: With empty CookieJar, no Cookie header added to redirect")]
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

    [Fact(DisplayName = "RFC9110-15.4-RH-051: Domain cookie re-evaluated for subdomain redirect")]
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


    [Fact(DisplayName = "RFC9110-15.4-RH-052: Redirect from HTTP/2 request preserves Version 2.0")]
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

    [Fact(DisplayName = "RFC9110-15.4-RH-053: Redirect from HTTP/1.0 request preserves Version 1.0")]
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

    [Fact(DisplayName = "RFC9110-15.4-RH-054: Cross-origin redirect preserves Version")]
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

    [Fact(DisplayName = "RFC9110-15.4-RH-055: Redirect default Version is HTTP/1.1 when original is 1.1")]
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

    [Fact(DisplayName = "RFC9110-15.4-RH-056: CookieJar overload also preserves Version")]
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


    [Fact(DisplayName = "RFC9110-15.4-RH-057: Request A exhausts 5 redirects, Request B starts fresh with 0")]
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

    [Fact(DisplayName = "RFC9110-15.4-RH-058: Request A and B can visit same URI independently without false loop")]
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


    private static HttpResponseMessage BuildRedirect(HttpStatusCode statusCode, string location)
    {
        var response = new HttpResponseMessage(statusCode);
        response.Headers.TryAddWithoutValidation("Location", location);
        return response;
    }
}
