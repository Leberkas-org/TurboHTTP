using System.Net;
using TurboHttp.Protocol.RFC9110;

namespace TurboHttp.Tests.RFC9110;

/// <summary>
/// RFC 9110 §15.4 — Redirect security tests.
/// Covers HTTPS→HTTP downgrade protection, redirect loop detection,
/// max redirect depth enforcement, URL normalization for comparison,
/// and edge cases around query strings, fragments, and host casing.
/// </summary>
/// <remarks>
/// Class under test: <see cref="RedirectHandler"/>.
/// These tests verify FR-5 (HTTPS→HTTP downgrade) and FR-6 (loop/depth).
/// </remarks>
public sealed class RedirectSecurityTests
{
    // ── HTTPS→HTTP Downgrade Protection ─────────────────────────────────────

    [Fact(Timeout = 5000, DisplayName = "RFC9110-15.4-RS-001: HTTPS→HTTP redirect rejected by default")]
    public void Should_RejectDowngrade_When_HttpsToHttp_DefaultPolicy()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Get, "https://example.com/secure");
        var response = BuildRedirect(HttpStatusCode.Found, "http://example.com/insecure");

        var ex = Assert.Throws<RedirectException>(() =>
            handler.BuildRedirectRequest(original, response));

        Assert.Equal(RedirectError.ProtocolDowngrade, ex.Error);
    }

    [Fact(Timeout = 5000, DisplayName = "RFC9110-15.4-RS-002: HTTPS→HTTP allowed with AllowHttpsToHttpDowngrade = true")]
    public void Should_AllowDowngrade_When_PolicyPermits()
    {
        var handler = new RedirectHandler(new RedirectPolicy { AllowHttpsToHttpDowngrade = true });
        var original = new HttpRequestMessage(HttpMethod.Get, "https://example.com/secure");
        var response = BuildRedirect(HttpStatusCode.Found, "http://example.com/page");

        var redirected = handler.BuildRedirectRequest(original, response);

        Assert.NotNull(redirected);
        Assert.Equal("http://example.com/page", redirected.RequestUri?.AbsoluteUri);
    }

    [Fact(Timeout = 5000, DisplayName = "RFC9110-15.4-RS-003: HTTPS→HTTPS allowed (same scheme, no downgrade)")]
    public void Should_AllowRedirect_When_HttpsToHttps()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Get, "https://example.com/page");
        var response = BuildRedirect(HttpStatusCode.Found, "https://example.com/other");

        var redirected = handler.BuildRedirectRequest(original, response);

        Assert.NotNull(redirected);
        Assert.Equal("https://example.com/other", redirected.RequestUri?.AbsoluteUri);
    }

    [Fact(Timeout = 5000, DisplayName = "RFC9110-15.4-RS-004: HTTP→HTTP allowed (no scheme change)")]
    public void Should_AllowRedirect_When_HttpToHttp()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page");
        var response = BuildRedirect(HttpStatusCode.Found, "http://example.com/other");

        var redirected = handler.BuildRedirectRequest(original, response);

        Assert.NotNull(redirected);
        Assert.Equal("http://example.com/other", redirected.RequestUri?.AbsoluteUri);
    }

    [Fact(Timeout = 5000, DisplayName = "RFC9110-15.4-RS-005: HTTP→HTTPS allowed (upgrade encouraged)")]
    public void Should_AllowRedirect_When_HttpToHttps()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page");
        var response = BuildRedirect(HttpStatusCode.Found, "https://example.com/page");

        var redirected = handler.BuildRedirectRequest(original, response);

        Assert.NotNull(redirected);
        Assert.Equal("https://example.com/page", redirected.RequestUri?.AbsoluteUri);
    }

    [Theory(Timeout = 5000, DisplayName = "RFC9110-15.4-RS-006: HTTPS→HTTP downgrade blocked for all redirect status codes")]
    [InlineData(HttpStatusCode.MovedPermanently)]     // 301
    [InlineData(HttpStatusCode.Found)]                // 302
    [InlineData(HttpStatusCode.SeeOther)]             // 303
    [InlineData(HttpStatusCode.TemporaryRedirect)]    // 307
    [InlineData(HttpStatusCode.PermanentRedirect)]    // 308
    public void Should_BlockDowngrade_ForAllRedirectStatusCodes(HttpStatusCode statusCode)
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Get, "https://example.com/secure");
        var response = BuildRedirect(statusCode, "http://example.com/insecure");

        var ex = Assert.Throws<RedirectException>(() =>
            handler.BuildRedirectRequest(original, response));

        Assert.Equal(RedirectError.ProtocolDowngrade, ex.Error);
    }

    [Fact(Timeout = 5000, DisplayName = "RFC9110-15.4-RS-007: Downgrade error message mentions 'blocked'")]
    public void Should_IncludeBlockedInMessage_When_DowngradeRejected()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Get, "https://example.com/page");
        var response = BuildRedirect(HttpStatusCode.Found, "http://example.com/page");

        var ex = Assert.Throws<RedirectException>(() =>
            handler.BuildRedirectRequest(original, response));

        Assert.Contains("RFC 9110 §15.4: Redirect from HTTPS to HTTP is not", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Timeout = 5000, DisplayName = "RFC9110-15.4-RS-008: HTTPS→HTTP downgrade to different host also blocked")]
    public void Should_BlockDowngrade_When_CrossOriginHttpsToHttp()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Get, "https://secure.example.com/page");
        var response = BuildRedirect(HttpStatusCode.Found, "http://insecure.example.com/page");

        var ex = Assert.Throws<RedirectException>(() =>
            handler.BuildRedirectRequest(original, response));

        Assert.Equal(RedirectError.ProtocolDowngrade, ex.Error);
    }

    // ── Loop Detection ──────────────────────────────────────────────────────

    [Fact(Timeout = 5000, DisplayName = "RFC9110-15.4-RS-009: Self-redirect (A → A) detected and rejected")]
    public void Should_DetectLoop_When_SelfRedirect()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page");
        var response = BuildRedirect(HttpStatusCode.Found, "http://example.com/page");

        var ex = Assert.Throws<RedirectException>(() =>
            handler.BuildRedirectRequest(original, response));

        Assert.Equal(RedirectError.RedirectLoop, ex.Error);
    }

    [Fact(Timeout = 5000, DisplayName = "RFC9110-15.4-RS-010: A → B → A cycle detected and rejected")]
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

    [Fact(Timeout = 5000, DisplayName = "RFC9110-15.4-RS-011: A → B → C → B detected and rejected (re-visit)")]
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

    [Fact(Timeout = 5000, DisplayName = "RFC9110-15.4-RS-012: Loop error message contains target URI")]
    public void Should_IncludeTargetUri_InLoopErrorMessage()
    {
        var handler = new RedirectHandler();
        var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/target");
        var response = BuildRedirect(HttpStatusCode.Found, "http://example.com/target");

        var ex = Assert.Throws<RedirectException>(() =>
            handler.BuildRedirectRequest(original, response));

        Assert.Contains("example.com/target", ex.Message);
    }

    // ── Max Redirect Depth ──────────────────────────────────────────────────

    [Fact(Timeout = 5000, DisplayName = "RFC9110-15.4-RS-013: 5 redirects accepted with default policy")]
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

    [Fact(Timeout = 5000, DisplayName = "RFC9110-15.4-RS-014: 11th redirect rejected with default policy (depth exceeded)")]
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

    [Fact(Timeout = 5000, DisplayName = "RFC9110-15.4-RS-015: Max depth error message contains the limit value")]
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

    [Theory(Timeout = 5000, DisplayName = "RFC9110-15.4-RS-016: Custom max depth is respected")]
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

    // ── URL Normalization for Comparison ─────────────────────────────────────

    [Fact(Timeout = 5000, DisplayName = "RFC9110-15.4-RS-017: Query string variations treated as different URLs")]
    public void Should_TreatDifferentQueryStrings_AsDifferentUrls()
    {
        var handler = new RedirectHandler(new RedirectPolicy { MaxRedirects = 10 });

        var req1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page?v=1");
        var res1 = BuildRedirect(HttpStatusCode.Found, "http://example.com/page?v=2");
        var redirected = handler.BuildRedirectRequest(req1, res1);

        Assert.NotNull(redirected);
        Assert.Equal("http://example.com/page?v=2", redirected.RequestUri?.AbsoluteUri);
    }

    [Fact(Timeout = 5000, DisplayName = "RFC9110-15.4-RS-018: Same query string revisit detected as loop")]
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

    [Fact(Timeout = 5000, DisplayName = "RFC9110-15.4-RS-019: Fragment ignored in URL comparison")]
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

    [Fact(Timeout = 5000, DisplayName = "RFC9110-15.4-RS-020: Case-insensitive host matching detects loop")]
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

    [Fact(Timeout = 5000, DisplayName = "RFC9110-15.4-RS-021: Case-insensitive scheme matching detects loop")]
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

    [Fact(Timeout = 5000, DisplayName = "RFC9110-15.4-RS-022: Case-sensitive path — different case is not a loop")]
    public void Should_NotDetectLoop_When_PathCaseDiffers()
    {
        var handler = new RedirectHandler(new RedirectPolicy { MaxRedirects = 10 });

        var req = new HttpRequestMessage(HttpMethod.Get, "http://example.com/Page");
        var res = BuildRedirect(HttpStatusCode.Found, "http://example.com/page");
        var redirected = handler.BuildRedirectRequest(req, res);

        Assert.NotNull(redirected);
        Assert.Equal("http://example.com/page", redirected.RequestUri?.AbsoluteUri);
    }

    [Fact(Timeout = 5000, DisplayName = "RFC9110-15.4-RS-023: Default port normalization — port 80 matches implicit port")]
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

    [Fact(Timeout = 5000, DisplayName = "RFC9110-15.4-RS-024: Different ports treated as different URLs")]
    public void Should_NotDetectLoop_When_PortsDiffer()
    {
        var handler = new RedirectHandler(new RedirectPolicy { MaxRedirects = 10 });

        var req = new HttpRequestMessage(HttpMethod.Get, "http://example.com:8080/page");
        var res = BuildRedirect(HttpStatusCode.Found, "http://example.com:9090/page");
        var redirected = handler.BuildRedirectRequest(req, res);

        Assert.NotNull(redirected);
        Assert.Equal("http://example.com:9090/page", redirected.RequestUri?.AbsoluteUri);
    }

    // ── Combined Scenarios ──────────────────────────────────────────────────

    [Fact(Timeout = 5000, DisplayName = "RFC9110-15.4-RS-025: Multi-hop chain across domains succeeds within limit")]
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

    [Fact(Timeout = 5000, DisplayName = "RFC9110-15.4-RS-026: Downgrade check runs before loop check")]
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

    [Fact(Timeout = 5000, DisplayName = "RFC9110-15.4-RS-027: Reset clears loop history and redirect count")]
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

    [Fact(Timeout = 5000, DisplayName = "RFC9110-15.4-RS-028: HTTPS port 443 normalization — explicit 443 matches implicit")]
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

    [Fact(Timeout = 5000, DisplayName = "RFC9110-15.4-RS-029: Default policy has AllowHttpsToHttpDowngrade = false")]
    public void Should_DefaultPolicyBlockDowngrade()
    {
        Assert.False(RedirectPolicy.Default.AllowHttpsToHttpDowngrade);
    }

    [Fact(Timeout = 5000, DisplayName = "RFC9110-15.4-RS-030: Default policy has MaxRedirects = 10")]
    public void Should_DefaultPolicyMaxRedirects5()
    {
        Assert.Equal(10, RedirectPolicy.Default.MaxRedirects);
    }

    // ── Helper ──────────────────────────────────────────────────────────────

    private static HttpResponseMessage BuildRedirect(HttpStatusCode statusCode, string location)
    {
        var response = new HttpResponseMessage(statusCode);
        response.Headers.TryAddWithoutValidation("Location", location);
        return response;
    }
}
