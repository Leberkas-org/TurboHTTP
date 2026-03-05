using System.Net;
using TurboHttp.Protocol;

namespace TurboHttp.Tests.Integration;

/// <summary>
/// RFC 9110 §15.4 — Redirect handling tests.
/// Covers all redirect status codes, method rewriting, body preservation,
/// loop detection, max redirect enforcement, cross-origin security, and
/// HTTPS-to-HTTP downgrade protection.
/// </summary>
public sealed class RedirectHandlerTests
{
    // ── IsRedirect ────────────────────────────────────────────────────────────

    [Theory(DisplayName = "RH-001: IsRedirect returns true for redirect status codes")]
    [InlineData(301)]
    [InlineData(302)]
    [InlineData(303)]
    [InlineData(307)]
    [InlineData(308)]
    public void IsRedirect_Returns_True_For_Redirect_Status_Codes(int statusCode)
    {
        var response = new HttpResponseMessage((HttpStatusCode)statusCode);
        Assert.True(RedirectHandler.IsRedirect(response));
    }

    [Theory(DisplayName = "RH-002: IsRedirect returns false for non-redirect status codes")]
    [InlineData(200)]
    [InlineData(201)]
    [InlineData(304)]
    [InlineData(400)]
    [InlineData(404)]
    [InlineData(500)]
    public void IsRedirect_Returns_False_For_Non_Redirect_Status_Codes(int statusCode)
    {
        var response = new HttpResponseMessage((HttpStatusCode)statusCode);
        Assert.False(RedirectHandler.IsRedirect(response));
    }

    // ── 303 See Other: Always rewrite to GET, no body ─────────────────────────

    [Fact(DisplayName = "RH-003: 303 rewrites POST to GET and drops body")]
    public void SeeOther_303_Rewrites_Post_To_Get()
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

    [Fact(DisplayName = "RH-004: 303 rewrites PUT to GET and drops body")]
    public void SeeOther_303_Rewrites_Put_To_Get()
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

    [Fact(DisplayName = "RH-005: 303 rewrites DELETE to GET")]
    public void SeeOther_303_Rewrites_Delete_To_Get()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Delete, "http://example.com/resource");
        var response = BuildRedirect(HttpStatusCode.SeeOther, "http://example.com/new");

        var redirected = handler.BuildRedirectRequest(original, response);

        Assert.Equal(HttpMethod.Get, redirected.Method);
    }

    // ── 307 Temporary Redirect: Preserve method and body ─────────────────────

    [Fact(DisplayName = "RH-006: 307 preserves POST method and body")]
    public void TemporaryRedirect_307_Preserves_Post_Method_And_Body()
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

    [Fact(DisplayName = "RH-007: 307 preserves PUT method and body")]
    public void TemporaryRedirect_307_Preserves_Put_Method_And_Body()
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

    [Fact(DisplayName = "RH-008: 307 preserves DELETE method")]
    public void TemporaryRedirect_307_Preserves_Delete_Method()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Delete, "http://example.com/resource");
        var response = BuildRedirect(HttpStatusCode.TemporaryRedirect, "http://example.com/new");

        var redirected = handler.BuildRedirectRequest(original, response);

        Assert.Equal(HttpMethod.Delete, redirected.Method);
    }

    // ── 308 Permanent Redirect: Preserve method and body ─────────────────────

    [Fact(DisplayName = "RH-009: 308 preserves POST method and body")]
    public void PermanentRedirect_308_Preserves_Post_Method_And_Body()
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

    [Fact(DisplayName = "RH-010: 308 preserves PATCH method and body")]
    public void PermanentRedirect_308_Preserves_Patch_Method_And_Body()
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

    // ── 301/302: Historical GET rewrite for POST ──────────────────────────────

    [Theory(DisplayName = "RH-011: 301/302 rewrites POST to GET (historical behavior)")]
    [InlineData(301)]
    [InlineData(302)]
    public void MovedPermanently_302_Rewrites_Post_To_Get(int statusCode)
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

    [Theory(DisplayName = "RH-012: 301/302 preserves GET method")]
    [InlineData(301)]
    [InlineData(302)]
    public void MovedPermanently_302_Preserves_Get_Method(int statusCode)
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        var response = BuildRedirect((HttpStatusCode)statusCode, "http://example.com/new");

        var redirected = handler.BuildRedirectRequest(original, response);

        Assert.Equal(HttpMethod.Get, redirected.Method);
    }

    [Theory(DisplayName = "RH-013: 301/302 preserves HEAD method")]
    [InlineData(301)]
    [InlineData(302)]
    public void MovedPermanently_302_Preserves_Head_Method(int statusCode)
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Head, "http://example.com/resource");
        var response = BuildRedirect((HttpStatusCode)statusCode, "http://example.com/new");

        var redirected = handler.BuildRedirectRequest(original, response);

        Assert.Equal(HttpMethod.Head, redirected.Method);
    }

    // ── Location header resolution ────────────────────────────────────────────

    [Fact(DisplayName = "RH-014: Absolute Location URI used as-is")]
    public void BuildRedirectRequest_Uses_Absolute_Location()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page");
        var response = BuildRedirect(HttpStatusCode.MovedPermanently, "http://other.com/new-page");

        var redirected = handler.BuildRedirectRequest(original, response);

        Assert.Equal("http://other.com/new-page", redirected.RequestUri?.AbsoluteUri);
    }

    [Fact(DisplayName = "RH-015: Relative Location URI resolved against request URI")]
    public void BuildRedirectRequest_Resolves_Relative_Location()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/api/v1/resource");
        var response = BuildRedirect(HttpStatusCode.MovedPermanently, "/api/v2/resource");

        var redirected = handler.BuildRedirectRequest(original, response);

        Assert.Equal("http://example.com/api/v2/resource", redirected.RequestUri?.AbsoluteUri);
    }

    [Fact(DisplayName = "RH-016: Relative path Location URI resolved correctly")]
    public void BuildRedirectRequest_Resolves_Relative_Path_Location()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/dir/page");
        var response = BuildRedirect(HttpStatusCode.Found, "other-page");

        var redirected = handler.BuildRedirectRequest(original, response);

        Assert.NotNull(redirected.RequestUri);
        Assert.Equal("example.com", redirected.RequestUri.Host);
    }

    // ── Max redirects ─────────────────────────────────────────────────────────

    [Fact(DisplayName = "RH-017: Throws RedirectException when max redirects exceeded")]
    public void BuildRedirectRequest_Throws_When_Max_Redirects_Exceeded()
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

    [Fact(DisplayName = "RH-018: Throws RedirectException after default max 10 redirects")]
    public void BuildRedirectRequest_Throws_When_Default_Max_Redirects_Exceeded()
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

    [Fact(DisplayName = "RH-019: RedirectCount tracks number of redirects")]
    public void RedirectCount_Tracks_Number_Of_Redirects()
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

    // ── Loop detection ────────────────────────────────────────────────────────

    [Fact(DisplayName = "RH-020: Throws RedirectException on direct redirect loop")]
    public void BuildRedirectRequest_Throws_On_Direct_Loop()
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

    [Fact(DisplayName = "RH-021: Throws RedirectException on self-redirect (A → A)")]
    public void BuildRedirectRequest_Throws_On_Self_Redirect()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page");
        var response = BuildRedirect(HttpStatusCode.Found, "http://example.com/page");

        var ex = Assert.Throws<RedirectException>(() =>
            handler.BuildRedirectRequest(original, response));

        Assert.Equal(RedirectError.RedirectLoop, ex.Error);
    }

    // ── Missing Location header ───────────────────────────────────────────────

    [Fact(DisplayName = "RH-022: Throws RedirectException when Location header is missing")]
    public void BuildRedirectRequest_Throws_When_Location_Header_Missing()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page");
        var response = new HttpResponseMessage(HttpStatusCode.MovedPermanently);

        var ex = Assert.Throws<RedirectException>(() =>
            handler.BuildRedirectRequest(original, response));

        Assert.Equal(RedirectError.MissingLocationHeader, ex.Error);
    }

    [Fact(DisplayName = "RH-023: 308 preserves GET method (no body rewrite)")]
    public void PermanentRedirect_308_Preserves_Get_Method()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        var response = BuildRedirect(HttpStatusCode.PermanentRedirect, "http://example.com/new");

        var redirected = handler.BuildRedirectRequest(original, response);

        Assert.Equal(HttpMethod.Get, redirected.Method);
        Assert.Null(redirected.Content);
    }

    // ── Cross-origin security: Authorization header stripping ────────────────

    [Fact(DisplayName = "RH-024: Strips Authorization header on cross-origin redirect")]
    public void BuildRedirectRequest_Strips_Authorization_On_Cross_Origin()
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

    [Fact(DisplayName = "RH-025: Preserves Authorization header on same-origin redirect")]
    public void BuildRedirectRequest_Preserves_Authorization_On_Same_Origin()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/old");
        original.Headers.TryAddWithoutValidation("Authorization", "Bearer secret-token");
        var response = BuildRedirect(HttpStatusCode.Found, "http://example.com/new");

        var redirected = handler.BuildRedirectRequest(original, response);

        Assert.True(redirected.Headers.Contains("Authorization"),
            "Authorization header should be preserved for same-origin redirects");
    }

    [Fact(DisplayName = "RH-026: Strips Authorization header when scheme changes (HTTPS→HTTP)")]
    public void BuildRedirectRequest_Strips_Authorization_When_Scheme_Changes()
    {
        var handler = new RedirectHandler(new RedirectPolicy { AllowHttpsToHttpDowngrade = true });
        var original = new HttpRequestMessage(HttpMethod.Get, "https://example.com/api");
        original.Headers.TryAddWithoutValidation("Authorization", "Bearer token");
        var response = BuildRedirect(HttpStatusCode.Found, "http://example.com/api");

        var redirected = handler.BuildRedirectRequest(original, response);

        Assert.False(redirected.Headers.Contains("Authorization"),
            "Authorization must be stripped when scheme changes");
    }

    [Fact(DisplayName = "RH-027: Strips Authorization header when port changes")]
    public void BuildRedirectRequest_Strips_Authorization_When_Port_Changes()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com:8080/api");
        original.Headers.TryAddWithoutValidation("Authorization", "Bearer token");
        var response = BuildRedirect(HttpStatusCode.Found, "http://example.com:9090/api");

        var redirected = handler.BuildRedirectRequest(original, response);

        Assert.False(redirected.Headers.Contains("Authorization"),
            "Authorization must be stripped when port changes");
    }

    // ── HTTPS → HTTP downgrade ────────────────────────────────────────────────

    [Fact(DisplayName = "RH-028: Throws RedirectDowngradeException on HTTPS to HTTP redirect")]
    public void BuildRedirectRequest_Throws_On_Https_To_Http_Downgrade()
    {
        var handler = new RedirectHandler(); // AllowHttpsToHttpDowngrade = false by default
        var original = new HttpRequestMessage(HttpMethod.Get, "https://example.com/secure");
        var response = BuildRedirect(HttpStatusCode.Found, "http://example.com/insecure");

        Assert.Throws<RedirectDowngradeException>(() =>
            handler.BuildRedirectRequest(original, response));
    }

    [Fact(DisplayName = "RH-029: Allows HTTPS to HTTP downgrade when policy permits")]
    public void BuildRedirectRequest_Allows_Downgrade_When_Policy_Permits()
    {
        var handler = new RedirectHandler(new RedirectPolicy { AllowHttpsToHttpDowngrade = true });
        var original = new HttpRequestMessage(HttpMethod.Get, "https://example.com/secure");
        var response = BuildRedirect(HttpStatusCode.Found, "http://example.com/insecure");

        var redirected = handler.BuildRedirectRequest(original, response);

        Assert.Equal("http://example.com/insecure", redirected.RequestUri?.AbsoluteUri);
    }

    [Fact(DisplayName = "RH-030: Allows HTTP to HTTPS upgrade (no downgrade block)")]
    public void BuildRedirectRequest_Allows_Http_To_Https_Upgrade()
    {
        var handler = new RedirectHandler(); // only blocks downgrade, not upgrade
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page");
        var response = BuildRedirect(HttpStatusCode.Found, "https://example.com/page");

        var redirected = handler.BuildRedirectRequest(original, response);

        Assert.Equal("https://example.com/page", redirected.RequestUri?.AbsoluteUri);
    }

    // ── Reset ─────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RH-031: Reset clears redirect count and history")]
    public void Reset_Clears_Redirect_Count_And_History()
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

    [Fact(DisplayName = "RH-032: Reset allows previously visited URI to be visited again")]
    public void Reset_Allows_Previously_Visited_Uri_After_Reset()
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

    // ── Custom headers preserved ──────────────────────────────────────────────

    [Fact(DisplayName = "RH-033: Non-sensitive headers are copied on redirect")]
    public void BuildRedirectRequest_Copies_Non_Sensitive_Headers()
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

    [Fact(DisplayName = "RH-034: Host header is not blindly copied on redirect")]
    public void BuildRedirectRequest_Does_Not_Copy_Host_Header()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page");
        original.Headers.TryAddWithoutValidation("Host", "example.com");
        var response = BuildRedirect(HttpStatusCode.Found, "http://other.com/page");

        var redirected = handler.BuildRedirectRequest(original, response);

        Assert.False(redirected.Headers.Contains("Host"),
            "Host header must not be blindly copied — it is set from the new URI");
    }

    // ── Policy defaults ───────────────────────────────────────────────────────

    [Fact(DisplayName = "RH-035: Default policy has MaxRedirects = 10")]
    public void Default_Policy_Has_MaxRedirects_10()
    {
        Assert.Equal(10, RedirectPolicy.Default.MaxRedirects);
    }

    [Fact(DisplayName = "RH-036: Default policy does not allow HTTPS to HTTP downgrade")]
    public void Default_Policy_Does_Not_Allow_Downgrade()
    {
        Assert.False(RedirectPolicy.Default.AllowHttpsToHttpDowngrade);
    }

    // ── Null guard ────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RH-037: IsRedirect throws ArgumentNullException for null response")]
    public void IsRedirect_Throws_For_Null_Response()
    {
        Assert.Throws<ArgumentNullException>(() =>
            RedirectHandler.IsRedirect(null!));
    }

    [Fact(DisplayName = "RH-038: BuildRedirectRequest throws ArgumentNullException for null original")]
    public void BuildRedirectRequest_Throws_For_Null_Original()
    {
        var handler = new RedirectHandler();
        var response = BuildRedirect(HttpStatusCode.Found, "http://example.com/b");

        Assert.Throws<ArgumentNullException>(() =>
            handler.BuildRedirectRequest(null!, response));
    }

    [Fact(DisplayName = "RH-039: BuildRedirectRequest throws ArgumentNullException for null response")]
    public void BuildRedirectRequest_Throws_For_Null_Response()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/a");

        Assert.Throws<ArgumentNullException>(() =>
            handler.BuildRedirectRequest(original, null!));
    }

    // ── Cookie re-evaluation on redirect ─────────────────────────────────────

    [Fact(DisplayName = "RH-040: Cookie header is stripped when building redirect request")]
    public void BuildRedirectRequest_Strips_Cookie_Header()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page");
        original.Headers.TryAddWithoutValidation("Cookie", "session=abc123");
        var response = BuildRedirect(HttpStatusCode.Found, "http://example.com/new");

        var redirected = handler.BuildRedirectRequest(original, response);

        Assert.False(redirected.Headers.Contains("Cookie"),
            "Cookie header must not be blindly forwarded — it must be re-evaluated per redirect URI");
    }

    [Fact(DisplayName = "RH-041: With CookieJar, cookies re-applied for same-origin redirect")]
    public void BuildRedirectRequest_WithJar_ReappliesCookies_SameOrigin()
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

    [Fact(DisplayName = "RH-042: With CookieJar, cookies NOT re-applied for different domain")]
    public void BuildRedirectRequest_WithJar_DoesNotReapplyCookies_CrossOrigin()
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

    [Fact(DisplayName = "RH-043: With CookieJar, Set-Cookie from redirect response is processed")]
    public void BuildRedirectRequest_WithJar_ProcessesSetCookieFromRedirectResponse()
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

    [Fact(DisplayName = "RH-044: With CookieJar, Set-Cookie from redirect applied to next hop")]
    public void BuildRedirectRequest_WithJar_SetCookieAppliedToRedirectRequest()
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

    [Fact(DisplayName = "RH-045: With CookieJar, Secure cookies only sent to HTTPS redirect")]
    public void BuildRedirectRequest_WithJar_SecureCookiesOnlySentToHttps()
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

    [Fact(DisplayName = "RH-046: With CookieJar, Secure cookies sent when redirect stays on HTTPS")]
    public void BuildRedirectRequest_WithJar_SecureCookiesSentOverHttps()
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

    [Fact(DisplayName = "RH-047: With CookieJar, path-restricted cookie not sent for non-matching path")]
    public void BuildRedirectRequest_WithJar_PathRestrictedCookieNotSentForNonMatchingPath()
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

    [Fact(DisplayName = "RH-048: With CookieJar, path-restricted cookie sent for matching path")]
    public void BuildRedirectRequest_WithJar_PathRestrictedCookieSentForMatchingPath()
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

    [Fact(DisplayName = "RH-049: BuildRedirectRequest(jar) throws ArgumentNullException for null jar")]
    public void BuildRedirectRequest_WithJar_Throws_For_Null_CookieJar()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/a");
        var response = BuildRedirect(HttpStatusCode.Found, "http://example.com/b");

        Assert.Throws<ArgumentNullException>(() =>
            handler.BuildRedirectRequest(original, response, null!));
    }

    [Fact(DisplayName = "RH-050: With empty CookieJar, no Cookie header added to redirect")]
    public void BuildRedirectRequest_WithEmptyJar_NosCookieHeader()
    {
        var handler = new RedirectHandler();
        var jar = new CookieJar();

        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page");
        var response = BuildRedirect(HttpStatusCode.Found, "http://example.com/new");

        var redirected = handler.BuildRedirectRequest(original, response, jar);

        Assert.False(redirected.Headers.Contains("Cookie"),
            "Empty jar should result in no Cookie header");
    }

    [Fact(DisplayName = "RH-051: Domain cookie re-evaluated for subdomain redirect")]
    public void BuildRedirectRequest_WithJar_DomainCookieReappliedForSubdomainRedirect()
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

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static HttpResponseMessage BuildRedirect(HttpStatusCode statusCode, string location)
    {
        var response = new HttpResponseMessage(statusCode);
        response.Headers.TryAddWithoutValidation("Location", location);
        return response;
    }
}
