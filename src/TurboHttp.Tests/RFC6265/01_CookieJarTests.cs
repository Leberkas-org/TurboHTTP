using System.Net;
using TurboHttp.Protocol.RFC6265;

namespace TurboHttp.Tests.RFC6265;

/// <summary>
/// RFC 6265 — Cookie management tests.
/// Covers: Set-Cookie parsing, domain matching, path matching, host-only cookies,
/// Secure/HttpOnly attributes, Expires/Max-Age handling, SameSite, multiple cookies,
/// cookie replacement, expiry/deletion, and AddCookiesToRequest filtering.
/// </summary>
/// <remarks>
/// Class under test: <see cref="CookieJar"/>.
/// RFC 6265 §5: Cookie processing model — storage, retrieval, and attribute enforcement.
/// </remarks>
public sealed class CookieJarTests
{
    private static Uri Uri(string url) => new(url);

    private static HttpResponseMessage ResponseWithCookie(string setCookie)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.TryAddWithoutValidation("Set-Cookie", setCookie);
        return response;
    }


    [Fact(DisplayName = "RFC6265-5.3-CM-001: Basic name=value cookie is stored")]
    public void Should_StoreCookie_When_BasicNameValueCookie()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("session=abc123"));
        Assert.Equal(1, jar.Count);
    }

    [Fact(DisplayName = "RFC6265-5.3-CM-002: Cookie value is accessible when adding to request")]
    public void Should_AddCookieValueToRequest_When_CookieMatches()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("token=xyz"));

        var req = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        jar.AddCookiesToRequest(Uri("http://example.com/"), ref req);

        Assert.True(req.Headers.TryGetValues("Cookie", out var values));
        Assert.Contains("token=xyz", string.Join("", values));
    }

    [Fact(DisplayName = "RFC6265-5.3-CM-003: Malformed cookie (no '=') is ignored")]
    public void Should_IgnoreCookie_When_NoEqualsSign()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("invalidsyntax"));
        Assert.Equal(0, jar.Count);
    }

    [Fact(DisplayName = "RFC6265-5.3-CM-004: Cookie with empty name is ignored")]
    public void Should_IgnoreCookie_When_NameIsEmpty()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("=value"));
        Assert.Equal(0, jar.Count);
    }

    [Fact(DisplayName = "RFC6265-5.3-CM-005: Multiple Set-Cookie headers are all processed")]
    public void Should_ProcessAllCookies_When_MultipleSetCookieHeaders()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.TryAddWithoutValidation("Set-Cookie", "a=1");
        response.Headers.TryAddWithoutValidation("Set-Cookie", "b=2");
        response.Headers.TryAddWithoutValidation("Set-Cookie", "c=3");

        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/"), response);

        Assert.Equal(3, jar.Count);
    }


    [Fact(DisplayName = "RFC6265-5.1.3-CM-006: Host-only cookie (no Domain attr) matches exact host only")]
    public void Should_MatchExactHostOnly_When_HostOnlyCookie()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("id=1"));

        var req = new HttpRequestMessage(HttpMethod.Get, "http://sub.example.com/");
        jar.AddCookiesToRequest(Uri("http://sub.example.com/"), ref req);

        Assert.False(req.Headers.Contains("Cookie"));
    }

    [Fact(DisplayName = "RFC6265-5.1.3-CM-007: Host-only cookie matches same host")]
    public void Should_MatchSameHost_When_HostOnlyCookie()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("id=1"));

        var req = new HttpRequestMessage(HttpMethod.Get, "http://example.com/path");
        jar.AddCookiesToRequest(Uri("http://example.com/path"), ref req);

        Assert.True(req.Headers.Contains("Cookie"));
    }

    [Fact(DisplayName = "RFC6265-5.1.3-CM-008: Domain cookie matches subdomain")]
    public void Should_MatchSubdomain_When_DomainCookie()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("id=1; Domain=example.com"));

        var req = new HttpRequestMessage(HttpMethod.Get, "http://sub.example.com/");
        jar.AddCookiesToRequest(Uri("http://sub.example.com/"), ref req);

        Assert.True(req.Headers.Contains("Cookie"));
    }

    [Fact(DisplayName = "RFC6265-5.1.3-CM-009: Domain cookie does NOT match unrelated host (no naive EndsWith)")]
    public void Should_NotMatchUnrelatedHost_When_DomainCookie()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("id=1; Domain=example.com"));

        var req = new HttpRequestMessage(HttpMethod.Get, "http://notexample.com/");
        jar.AddCookiesToRequest(Uri("http://notexample.com/"), ref req);

        Assert.False(req.Headers.Contains("Cookie"));
    }

    [Fact(DisplayName = "RFC6265-5.1.3-CM-010: Domain cookie with leading dot is stored correctly (dot stripped)")]
    public void Should_StripLeadingDot_When_DomainCookieHasLeadingDot()
    {
        var jar = new CookieJar();
        // Leading dot should be stripped per RFC 6265 §5.2.3
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("id=1; Domain=.example.com"));

        var req = new HttpRequestMessage(HttpMethod.Get, "http://sub.example.com/");
        jar.AddCookiesToRequest(Uri("http://sub.example.com/"), ref req);

        Assert.True(req.Headers.Contains("Cookie"));
    }


    [Fact(DisplayName = "RFC6265-5.1.4-CM-011: Cookie with path=/api matches /api/users")]
    public void Should_MatchSubPath_When_PathCookie()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/api"), ResponseWithCookie("token=x; Path=/api"));

        var req = new HttpRequestMessage(HttpMethod.Get, "http://example.com/api/users");
        jar.AddCookiesToRequest(Uri("http://example.com/api/users"), ref req);

        Assert.True(req.Headers.Contains("Cookie"));
    }

    [Fact(DisplayName = "RFC6265-5.1.4-CM-012: Cookie with path=/api does NOT match /apiv2")]
    public void Should_NotMatchPartialLabel_When_PathCookie()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/api"), ResponseWithCookie("token=x; Path=/api"));

        var req = new HttpRequestMessage(HttpMethod.Get, "http://example.com/apiv2");
        jar.AddCookiesToRequest(Uri("http://example.com/apiv2"), ref req);

        Assert.False(req.Headers.Contains("Cookie"));
    }

    [Fact(DisplayName = "RFC6265-5.1.4-CM-013: Cookie with path=/ matches all paths")]
    public void Should_MatchAllPaths_When_PathIsRoot()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("global=1; Path=/"));

        var req = new HttpRequestMessage(HttpMethod.Get, "http://example.com/deep/nested/path");
        jar.AddCookiesToRequest(Uri("http://example.com/deep/nested/path"), ref req);

        Assert.True(req.Headers.Contains("Cookie"));
    }

    [Fact(DisplayName = "RFC6265-5.1.4-CM-014: Cookie with path=/foo/ (trailing slash) matches /foo/bar")]
    public void Should_MatchSubPath_When_PathHasTrailingSlash()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/foo/"), ResponseWithCookie("x=1; Path=/foo/"));

        var req = new HttpRequestMessage(HttpMethod.Get, "http://example.com/foo/bar");
        jar.AddCookiesToRequest(Uri("http://example.com/foo/bar"), ref req);

        Assert.True(req.Headers.Contains("Cookie"));
    }

    [Fact(DisplayName = "RFC6265-5.1.4-CM-015: Cookie path is correctly defaulted from request URI")]
    public void Should_ComputeDefaultPath_When_NoCookiePath()
    {
        var jar = new CookieJar();
        // Request to /foo/bar — default path should be /foo
        jar.ProcessResponse(Uri("http://example.com/foo/bar"), ResponseWithCookie("x=1"));

        // Should match /foo/baz (same directory)
        var req = new HttpRequestMessage(HttpMethod.Get, "http://example.com/foo/baz");
        jar.AddCookiesToRequest(Uri("http://example.com/foo/baz"), ref req);

        Assert.True(req.Headers.Contains("Cookie"));
    }


    [Fact(DisplayName = "RFC6265-5.3-CM-016: Secure cookie is NOT sent over HTTP")]
    public void Should_NotSendCookie_When_SecureCookieAndHttpScheme()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("https://example.com/"), ResponseWithCookie("sess=abc; Secure"));

        var req = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        jar.AddCookiesToRequest(Uri("http://example.com/"), ref req);

        Assert.False(req.Headers.Contains("Cookie"));
    }

    [Fact(DisplayName = "RFC6265-5.3-CM-017: Secure cookie IS sent over HTTPS")]
    public void Should_SendCookie_When_SecureCookieAndHttpsScheme()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("https://example.com/"), ResponseWithCookie("sess=abc; Secure"));

        var req = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        jar.AddCookiesToRequest(Uri("https://example.com/"), ref req);

        Assert.True(req.Headers.Contains("Cookie"));
    }

    [Fact(DisplayName = "RFC6265-5.3-CM-018: Non-secure cookie IS sent over HTTP")]
    public void Should_SendCookie_When_NonSecureCookieAndHttpScheme()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("pref=dark"));

        var req = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        jar.AddCookiesToRequest(Uri("http://example.com/"), ref req);

        Assert.True(req.Headers.Contains("Cookie"));
    }


    [Fact(DisplayName = "RFC6265-5.3-CM-019: HttpOnly cookie is stored with HttpOnly=true")]
    public void Should_StoreCookie_When_HttpOnlyAttribute()
    {
        var jar = new CookieJar();
        // We verify via behavior: HttpOnly cookies are still sent in HTTP requests (server-side flag)
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("session=s1; HttpOnly"));

        var req = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        jar.AddCookiesToRequest(Uri("http://example.com/"), ref req);

        // HttpOnly = restrict JS access; we still send it in HTTP requests
        Assert.True(req.Headers.Contains("Cookie"));
    }

    [Fact(DisplayName = "RFC6265-5.3-CM-020: Non-HttpOnly cookie is stored and sent")]
    public void Should_StoreAndSendCookie_When_NonHttpOnlyCookie()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("pref=light"));

        var req = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        jar.AddCookiesToRequest(Uri("http://example.com/"), ref req);

        Assert.True(req.Headers.Contains("Cookie"));
    }


    [Fact(DisplayName = "RFC6265-5.3-CM-021: Expired cookie (past Expires) is not sent")]
    public void Should_NotSendCookie_When_CookieIsExpired()
    {
        var jar = new CookieJar();
        // Expires in the distant past
        jar.ProcessResponse(Uri("http://example.com/"),
            ResponseWithCookie("old=x; Expires=Thu, 01 Jan 1970 00:00:00 GMT"));

        var req = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        jar.AddCookiesToRequest(Uri("http://example.com/"), ref req);

        Assert.False(req.Headers.Contains("Cookie"));
    }

    [Fact(DisplayName = "RFC6265-5.3-CM-022: Future Expires cookie IS sent")]
    public void Should_SendCookie_When_ExpiresInFuture()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/"),
            ResponseWithCookie("future=x; Expires=Thu, 01 Jan 2099 00:00:00 GMT"));

        var req = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        jar.AddCookiesToRequest(Uri("http://example.com/"), ref req);

        Assert.True(req.Headers.Contains("Cookie"));
    }

    [Fact(DisplayName = "RFC6265-5.3-CM-023: Max-Age=0 deletes existing cookie")]
    public void Should_DeleteCookie_When_MaxAgeIsZero()
    {
        var jar = new CookieJar();
        // First: add the cookie
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("session=abc"));
        Assert.Equal(1, jar.Count);

        // Then: delete it with Max-Age=0
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("session=abc; Max-Age=0"));
        Assert.Equal(0, jar.Count);
    }

    [Fact(DisplayName = "RFC6265-5.3-CM-024: Max-Age takes precedence over Expires")]
    public void Should_PreferMaxAge_When_BothMaxAgeAndExpiresPresent()
    {
        var jar = new CookieJar();
        // Expires says far future, but Max-Age=0 should win and delete
        jar.ProcessResponse(Uri("http://example.com/"),
            ResponseWithCookie("x=1; Expires=Thu, 01 Jan 2099 00:00:00 GMT; Max-Age=0"));

        Assert.Equal(0, jar.Count);
    }

    [Fact(DisplayName = "RFC6265-5.3-CM-025: Max-Age positive sets future expiry")]
    public void Should_SetFutureExpiry_When_MaxAgeIsPositive()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("x=1; Max-Age=3600"));
        Assert.Equal(1, jar.Count);

        var req = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        jar.AddCookiesToRequest(Uri("http://example.com/"), ref req);

        Assert.True(req.Headers.Contains("Cookie"));
    }


    [Fact(DisplayName = "RFC6265-5.3-CM-026: Cookie with same name+domain+path replaces existing cookie")]
    public void Should_ReplaceCookie_When_SameNameDomainPath()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("token=old"));
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("token=new"));

        Assert.Equal(1, jar.Count);

        var req = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        jar.AddCookiesToRequest(Uri("http://example.com/"), ref req);

        Assert.True(req.Headers.TryGetValues("Cookie", out var vals));
        Assert.Contains("token=new", string.Join("", vals));
    }

    [Fact(DisplayName = "RFC6265-5.3-CM-027: Cookies with same name but different paths coexist")]
    public void Should_AllowCoexistence_When_SameNameDifferentPaths()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/api/v1"), ResponseWithCookie("x=1; Path=/api/v1"));
        jar.ProcessResponse(Uri("http://example.com/api/v2"), ResponseWithCookie("x=2; Path=/api/v2"));

        Assert.Equal(2, jar.Count);
    }

    [Fact(DisplayName = "RFC6265-5.3-CM-028: Clear() removes all cookies")]
    public void Should_RemoveAllCookies_When_ClearCalled()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("a=1"));
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("b=2"));

        jar.Clear();

        Assert.Equal(0, jar.Count);
    }


    [Fact(DisplayName = "RFC6265-5.3-CM-029: SameSite=Strict is stored correctly")]
    public void Should_StoreCookie_When_SameSiteStrict()
    {
        // We verify the cookie is stored (enforcement is caller's responsibility)
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("id=1; SameSite=Strict"));
        Assert.Equal(1, jar.Count);
    }

    [Fact(DisplayName = "RFC6265-5.3-CM-030: SameSite=Lax is stored correctly")]
    public void Should_StoreCookie_When_SameSiteLax()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("id=1; SameSite=Lax"));
        Assert.Equal(1, jar.Count);
    }


    [Fact(DisplayName = "RFC6265-5.3-CM-031: Cookie with Domain for unrelated host is rejected")]
    public void Should_RejectCookie_When_DomainForUnrelatedHost()
    {
        var jar = new CookieJar();
        // Server at example.com tries to set a cookie for attacker.com — must be rejected
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("steal=1; Domain=attacker.com"));
        Assert.Equal(0, jar.Count);
    }

    [Fact(DisplayName = "RFC6265-5.3-CM-032: Cookie Domain=example.com accepted from sub.example.com")]
    public void Should_AcceptCookie_When_DomainIsSuperDomain()
    {
        var jar = new CookieJar();
        // sub.example.com can set a cookie for example.com
        jar.ProcessResponse(Uri("http://sub.example.com/"), ResponseWithCookie("id=1; Domain=example.com"));
        Assert.Equal(1, jar.Count);
    }

    [Fact(DisplayName = "RFC6265-5.3-CM-033: Cookie Domain=sub.example.com from example.com is rejected")]
    public void Should_RejectCookie_When_SubDomainSetByParent()
    {
        var jar = new CookieJar();
        // example.com cannot set a cookie for sub.example.com via Domain attr (not a parent)
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("id=1; Domain=sub.example.com"));
        Assert.Equal(0, jar.Count);
    }


    [Fact(DisplayName = "RFC6265-5.1.3-CM-034: Cookie from IP address is host-only")]
    public void Should_BeHostOnly_When_CookieFromIpAddress()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://192.168.1.1/"), ResponseWithCookie("id=1"));

        var req = new HttpRequestMessage(HttpMethod.Get, "http://192.168.1.1/");
        jar.AddCookiesToRequest(Uri("http://192.168.1.1/"), ref req);

        Assert.True(req.Headers.Contains("Cookie"));
    }

    [Fact(DisplayName = "RFC6265-5.1.3-CM-035: Domain cookie is not matched to IP address host")]
    public void Should_NotMatchIpAddress_When_DomainCookie()
    {
        // DomainMatches with IP address request host should return false for domain cookies
        Assert.False(CookieJar.DomainMatches("example.com", false, "192.168.1.1"));
    }


    [Theory(DisplayName = "RFC6265-5.1.3-CM-036: DomainMatches returns correct result for various combinations")]
    [InlineData("example.com", true, "example.com", true)]
    [InlineData("example.com", true, "sub.example.com", false)]
    [InlineData("example.com", false, "example.com", true)]
    [InlineData("example.com", false, "sub.example.com", true)]
    [InlineData("example.com", false, "notexample.com", false)]
    [InlineData("example.com", false, "other.com", false)]
    [InlineData("example.com", false, "192.168.1.1", false)]
    public void Should_ReturnCorrectResult_When_CheckingDomainMatches(string cookieDomain, bool isHostOnly, string requestHost, bool expected)
    {
        Assert.Equal(expected, CookieJar.DomainMatches(cookieDomain, isHostOnly, requestHost));
    }


    [Theory(DisplayName = "RFC6265-5.1.4-CM-037: PathMatches returns correct result for various combinations")]
    [InlineData("/", "/", true)]
    [InlineData("/", "/foo", true)]
    [InlineData("/", "/foo/bar", true)]
    [InlineData("/foo", "/foo", true)]
    [InlineData("/foo", "/foo/", true)]
    [InlineData("/foo", "/foo/bar", true)]
    [InlineData("/foo", "/foobar", false)]
    [InlineData("/foo/", "/foo/bar", true)]
    [InlineData("/api/v1", "/api/v1/users", true)]
    [InlineData("/api/v1", "/api/v2", false)]
    [InlineData("/api/v1", "/api/v10", false)]
    public void Should_ReturnCorrectResult_When_CheckingPathMatches(string cookiePath, string requestPath, bool expected)
    {
        Assert.Equal(expected, CookieJar.PathMatches(cookiePath, requestPath));
    }

    [Fact(DisplayName = "RFC6265-5.1.4-CM-038: Cookies sorted by path length (longer first) in Cookie header")]
    public void Should_SortByPathLengthLongerFirst_When_BuildingCookieHeader()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("root=1; Path=/"));
        jar.ProcessResponse(Uri("http://example.com/api"), ResponseWithCookie("api=2; Path=/api"));
        jar.ProcessResponse(Uri("http://example.com/api/v1"), ResponseWithCookie("v1=3; Path=/api/v1"));

        var req = new HttpRequestMessage(HttpMethod.Get, "http://example.com/api/v1/users");
        jar.AddCookiesToRequest(Uri("http://example.com/api/v1/users"), ref req);

        Assert.True(req.Headers.TryGetValues("Cookie", out var vals));
        var cookieHeader = string.Join("", vals);

        // v1=3 (path=/api/v1, length=7) should come before api=2 (path=/api, length=4) before root=1 (path=/, length=1)
        var idxV1 = cookieHeader.IndexOf("v1=3", StringComparison.Ordinal);
        var idxApi = cookieHeader.IndexOf("api=2", StringComparison.Ordinal);
        var idxRoot = cookieHeader.IndexOf("root=1", StringComparison.Ordinal);

        Assert.True(idxV1 < idxApi);
        Assert.True(idxApi < idxRoot);
    }


    [Fact(DisplayName = "RFC6265-5.3-CM-039: Cookie jar evaluates cookies for new URI on redirect")]
    public void Should_EvaluateCookiesForNewUri_When_Redirecting()
    {
        var jar = new CookieJar();
        // Cookie for original domain
        jar.ProcessResponse(Uri("http://original.com/"), ResponseWithCookie("origin=1"));
        // Cookie for redirect domain
        jar.ProcessResponse(Uri("http://redirect.com/"), ResponseWithCookie("redir=2"));

        // Redirect to redirect.com — only redir=2 should be sent
        var req = new HttpRequestMessage(HttpMethod.Get, "http://redirect.com/");
        jar.AddCookiesToRequest(Uri("http://redirect.com/"), ref req);

        Assert.True(req.Headers.TryGetValues("Cookie", out var vals));
        var header = string.Join("", vals);
        Assert.Contains("redir=2", header);
        Assert.DoesNotContain("origin=1", header);
    }

    [Fact(DisplayName = "RFC6265-5.3-CM-040: No cookies sent when jar has no matching cookies")]
    public void Should_NotSendCookies_When_NoCookiesMatchUri()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("id=1"));

        var req = new HttpRequestMessage(HttpMethod.Get, "http://other.com/");
        jar.AddCookiesToRequest(Uri("http://other.com/"), ref req);

        Assert.False(req.Headers.Contains("Cookie"));
    }


    [Theory(DisplayName = "RFC6265-5.3-CM-041: Various Expires date formats are parsed correctly")]
    [InlineData("Thu, 01 Jan 2099 00:00:00 GMT")]
    [InlineData("Thu, 01-Jan-2099 00:00:00 GMT")]
    public void Should_ParseExpires_When_VariousDateFormats(string expiresValue)
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie($"x=1; Expires={expiresValue}"));
        Assert.Equal(1, jar.Count);

        var req = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        jar.AddCookiesToRequest(Uri("http://example.com/"), ref req);
        Assert.True(req.Headers.Contains("Cookie"));
    }

    [Fact(DisplayName = "RFC6265-5.3-CM-042: Cookie with unrecognized Expires format is treated as session cookie")]
    public void Should_TreatAsSessionCookie_When_ExpiresFormatUnrecognized()
    {
        // If Expires can't be parsed, the cookie should still be stored as session cookie
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("x=1; Expires=garbage-date"));
        Assert.Equal(1, jar.Count);
    }
}
