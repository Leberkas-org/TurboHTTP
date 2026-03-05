using System.Net;
using TurboHttp.Protocol;

namespace TurboHttp.Tests.Integration;

/// <summary>
/// RFC 6265 — Cookie management tests.
/// Covers: Set-Cookie parsing, domain matching, path matching, host-only cookies,
/// Secure/HttpOnly attributes, Expires/Max-Age handling, SameSite, multiple cookies,
/// cookie replacement, expiry/deletion, and AddCookiesToRequest filtering.
/// </summary>
public sealed class CookieJarTests
{
    private static Uri Uri(string url) => new(url);

    private static HttpResponseMessage ResponseWithCookie(string setCookie)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.TryAddWithoutValidation("Set-Cookie", setCookie);
        return response;
    }

    // ── CM-001–CM-005: Basic cookie parsing ───────────────────────────────────

    [Fact(DisplayName = "CM-001: Basic name=value cookie is stored")]
    public void Basic_Cookie_Is_Stored()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("session=abc123"));
        Assert.Equal(1, jar.Count);
    }

    [Fact(DisplayName = "CM-002: Cookie value is accessible when adding to request")]
    public void Cookie_Value_Is_Added_To_Request()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("token=xyz"));

        var req = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        jar.AddCookiesToRequest(Uri("http://example.com/"), ref req);

        Assert.True(req.Headers.TryGetValues("Cookie", out var values));
        Assert.Contains("token=xyz", string.Join("", values));
    }

    [Fact(DisplayName = "CM-003: Malformed cookie (no '=') is ignored")]
    public void Malformed_Cookie_No_Equals_Is_Ignored()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("invalidsyntax"));
        Assert.Equal(0, jar.Count);
    }

    [Fact(DisplayName = "CM-004: Cookie with empty name is ignored")]
    public void Cookie_With_Empty_Name_Is_Ignored()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("=value"));
        Assert.Equal(0, jar.Count);
    }

    [Fact(DisplayName = "CM-005: Multiple Set-Cookie headers are all processed")]
    public void Multiple_SetCookie_Headers_Are_All_Processed()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.TryAddWithoutValidation("Set-Cookie", "a=1");
        response.Headers.TryAddWithoutValidation("Set-Cookie", "b=2");
        response.Headers.TryAddWithoutValidation("Set-Cookie", "c=3");

        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/"), response);

        Assert.Equal(3, jar.Count);
    }

    // ── CM-006–CM-010: Domain matching (RFC 6265 §5.1.3) ─────────────────────

    [Fact(DisplayName = "CM-006: Host-only cookie (no Domain attr) matches exact host only")]
    public void HostOnly_Cookie_Matches_Exact_Host_Only()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("id=1"));

        var req = new HttpRequestMessage(HttpMethod.Get, "http://sub.example.com/");
        jar.AddCookiesToRequest(Uri("http://sub.example.com/"), ref req);

        Assert.False(req.Headers.Contains("Cookie"));
    }

    [Fact(DisplayName = "CM-007: Host-only cookie matches same host")]
    public void HostOnly_Cookie_Matches_Same_Host()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("id=1"));

        var req = new HttpRequestMessage(HttpMethod.Get, "http://example.com/path");
        jar.AddCookiesToRequest(Uri("http://example.com/path"), ref req);

        Assert.True(req.Headers.Contains("Cookie"));
    }

    [Fact(DisplayName = "CM-008: Domain cookie matches subdomain")]
    public void Domain_Cookie_Matches_Subdomain()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("id=1; Domain=example.com"));

        var req = new HttpRequestMessage(HttpMethod.Get, "http://sub.example.com/");
        jar.AddCookiesToRequest(Uri("http://sub.example.com/"), ref req);

        Assert.True(req.Headers.Contains("Cookie"));
    }

    [Fact(DisplayName = "CM-009: Domain cookie does NOT match unrelated host (no naive EndsWith)")]
    public void Domain_Cookie_Does_Not_Match_Unrelated_Host()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("id=1; Domain=example.com"));

        var req = new HttpRequestMessage(HttpMethod.Get, "http://notexample.com/");
        jar.AddCookiesToRequest(Uri("http://notexample.com/"), ref req);

        Assert.False(req.Headers.Contains("Cookie"));
    }

    [Fact(DisplayName = "CM-010: Domain cookie with leading dot is stored correctly (dot stripped)")]
    public void Domain_Cookie_Leading_Dot_Is_Stripped()
    {
        var jar = new CookieJar();
        // Leading dot should be stripped per RFC 6265 §5.2.3
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("id=1; Domain=.example.com"));

        var req = new HttpRequestMessage(HttpMethod.Get, "http://sub.example.com/");
        jar.AddCookiesToRequest(Uri("http://sub.example.com/"), ref req);

        Assert.True(req.Headers.Contains("Cookie"));
    }

    // ── CM-011–CM-015: Path matching (RFC 6265 §5.1.4) ───────────────────────

    [Fact(DisplayName = "CM-011: Cookie with path=/api matches /api/users")]
    public void Path_Cookie_Matches_Sub_Path()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/api"), ResponseWithCookie("token=x; Path=/api"));

        var req = new HttpRequestMessage(HttpMethod.Get, "http://example.com/api/users");
        jar.AddCookiesToRequest(Uri("http://example.com/api/users"), ref req);

        Assert.True(req.Headers.Contains("Cookie"));
    }

    [Fact(DisplayName = "CM-012: Cookie with path=/api does NOT match /apiv2")]
    public void Path_Cookie_Does_Not_Match_Partial_Label()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/api"), ResponseWithCookie("token=x; Path=/api"));

        var req = new HttpRequestMessage(HttpMethod.Get, "http://example.com/apiv2");
        jar.AddCookiesToRequest(Uri("http://example.com/apiv2"), ref req);

        Assert.False(req.Headers.Contains("Cookie"));
    }

    [Fact(DisplayName = "CM-013: Cookie with path=/ matches all paths")]
    public void Path_Root_Matches_All_Paths()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("global=1; Path=/"));

        var req = new HttpRequestMessage(HttpMethod.Get, "http://example.com/deep/nested/path");
        jar.AddCookiesToRequest(Uri("http://example.com/deep/nested/path"), ref req);

        Assert.True(req.Headers.Contains("Cookie"));
    }

    [Fact(DisplayName = "CM-014: Cookie with path=/foo/ (trailing slash) matches /foo/bar")]
    public void Path_With_Trailing_Slash_Matches_Sub_Path()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/foo/"), ResponseWithCookie("x=1; Path=/foo/"));

        var req = new HttpRequestMessage(HttpMethod.Get, "http://example.com/foo/bar");
        jar.AddCookiesToRequest(Uri("http://example.com/foo/bar"), ref req);

        Assert.True(req.Headers.Contains("Cookie"));
    }

    [Fact(DisplayName = "CM-015: Cookie path is correctly defaulted from request URI")]
    public void Default_Path_Is_Computed_From_Request_URI()
    {
        var jar = new CookieJar();
        // Request to /foo/bar — default path should be /foo
        jar.ProcessResponse(Uri("http://example.com/foo/bar"), ResponseWithCookie("x=1"));

        // Should match /foo/baz (same directory)
        var req = new HttpRequestMessage(HttpMethod.Get, "http://example.com/foo/baz");
        jar.AddCookiesToRequest(Uri("http://example.com/foo/baz"), ref req);

        Assert.True(req.Headers.Contains("Cookie"));
    }

    // ── CM-016–CM-020: Secure attribute ──────────────────────────────────────

    [Fact(DisplayName = "CM-016: Secure cookie is NOT sent over HTTP")]
    public void Secure_Cookie_Not_Sent_Over_Http()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("https://example.com/"), ResponseWithCookie("sess=abc; Secure"));

        var req = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        jar.AddCookiesToRequest(Uri("http://example.com/"), ref req);

        Assert.False(req.Headers.Contains("Cookie"));
    }

    [Fact(DisplayName = "CM-017: Secure cookie IS sent over HTTPS")]
    public void Secure_Cookie_Sent_Over_Https()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("https://example.com/"), ResponseWithCookie("sess=abc; Secure"));

        var req = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        jar.AddCookiesToRequest(Uri("https://example.com/"), ref req);

        Assert.True(req.Headers.Contains("Cookie"));
    }

    [Fact(DisplayName = "CM-018: Non-secure cookie IS sent over HTTP")]
    public void NonSecure_Cookie_Sent_Over_Http()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("pref=dark"));

        var req = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        jar.AddCookiesToRequest(Uri("http://example.com/"), ref req);

        Assert.True(req.Headers.Contains("Cookie"));
    }

    // ── CM-019–CM-020: HttpOnly attribute ─────────────────────────────────────

    [Fact(DisplayName = "CM-019: HttpOnly cookie is stored with HttpOnly=true")]
    public void HttpOnly_Cookie_Is_Stored()
    {
        var jar = new CookieJar();
        // We verify via behavior: HttpOnly cookies are still sent in HTTP requests (server-side flag)
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("session=s1; HttpOnly"));

        var req = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        jar.AddCookiesToRequest(Uri("http://example.com/"), ref req);

        // HttpOnly = restrict JS access; we still send it in HTTP requests
        Assert.True(req.Headers.Contains("Cookie"));
    }

    [Fact(DisplayName = "CM-020: Non-HttpOnly cookie is stored and sent")]
    public void NonHttpOnly_Cookie_Is_Stored_And_Sent()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("pref=light"));

        var req = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        jar.AddCookiesToRequest(Uri("http://example.com/"), ref req);

        Assert.True(req.Headers.Contains("Cookie"));
    }

    // ── CM-021–CM-025: Expires and Max-Age ────────────────────────────────────

    [Fact(DisplayName = "CM-021: Expired cookie (past Expires) is not sent")]
    public void Expired_Cookie_Is_Not_Sent()
    {
        var jar = new CookieJar();
        // Expires in the distant past
        jar.ProcessResponse(Uri("http://example.com/"),
            ResponseWithCookie("old=x; Expires=Thu, 01 Jan 1970 00:00:00 GMT"));

        var req = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        jar.AddCookiesToRequest(Uri("http://example.com/"), ref req);

        Assert.False(req.Headers.Contains("Cookie"));
    }

    [Fact(DisplayName = "CM-022: Future Expires cookie IS sent")]
    public void Future_Expires_Cookie_Is_Sent()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/"),
            ResponseWithCookie("future=x; Expires=Thu, 01 Jan 2099 00:00:00 GMT"));

        var req = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        jar.AddCookiesToRequest(Uri("http://example.com/"), ref req);

        Assert.True(req.Headers.Contains("Cookie"));
    }

    [Fact(DisplayName = "CM-023: Max-Age=0 deletes existing cookie")]
    public void MaxAge_Zero_Deletes_Existing_Cookie()
    {
        var jar = new CookieJar();
        // First: add the cookie
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("session=abc"));
        Assert.Equal(1, jar.Count);

        // Then: delete it with Max-Age=0
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("session=abc; Max-Age=0"));
        Assert.Equal(0, jar.Count);
    }

    [Fact(DisplayName = "CM-024: Max-Age takes precedence over Expires")]
    public void MaxAge_Takes_Precedence_Over_Expires()
    {
        var jar = new CookieJar();
        // Expires says far future, but Max-Age=0 should win and delete
        jar.ProcessResponse(Uri("http://example.com/"),
            ResponseWithCookie("x=1; Expires=Thu, 01 Jan 2099 00:00:00 GMT; Max-Age=0"));

        Assert.Equal(0, jar.Count);
    }

    [Fact(DisplayName = "CM-025: Max-Age positive sets future expiry")]
    public void MaxAge_Positive_Sets_Future_Expiry()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("x=1; Max-Age=3600"));
        Assert.Equal(1, jar.Count);

        var req = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        jar.AddCookiesToRequest(Uri("http://example.com/"), ref req);

        Assert.True(req.Headers.Contains("Cookie"));
    }

    // ── CM-026–CM-028: Cookie replacement ────────────────────────────────────

    [Fact(DisplayName = "CM-026: Cookie with same name+domain+path replaces existing cookie")]
    public void Cookie_Replacement_Same_Name_Domain_Path()
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

    [Fact(DisplayName = "CM-027: Cookies with same name but different paths coexist")]
    public void Cookies_Same_Name_Different_Paths_Coexist()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/api/v1"), ResponseWithCookie("x=1; Path=/api/v1"));
        jar.ProcessResponse(Uri("http://example.com/api/v2"), ResponseWithCookie("x=2; Path=/api/v2"));

        Assert.Equal(2, jar.Count);
    }

    [Fact(DisplayName = "CM-028: Clear() removes all cookies")]
    public void Clear_Removes_All_Cookies()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("a=1"));
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("b=2"));

        jar.Clear();

        Assert.Equal(0, jar.Count);
    }

    // ── CM-029–CM-030: SameSite attribute ─────────────────────────────────────

    [Fact(DisplayName = "CM-029: SameSite=Strict is stored correctly")]
    public void SameSite_Strict_Is_Stored()
    {
        // We verify the cookie is stored (enforcement is caller's responsibility)
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("id=1; SameSite=Strict"));
        Assert.Equal(1, jar.Count);
    }

    [Fact(DisplayName = "CM-030: SameSite=Lax is stored correctly")]
    public void SameSite_Lax_Is_Stored()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("id=1; SameSite=Lax"));
        Assert.Equal(1, jar.Count);
    }

    // ── CM-031–CM-033: Domain rejection ──────────────────────────────────────

    [Fact(DisplayName = "CM-031: Cookie with Domain for unrelated host is rejected")]
    public void Cookie_Domain_For_Unrelated_Host_Is_Rejected()
    {
        var jar = new CookieJar();
        // Server at example.com tries to set a cookie for attacker.com — must be rejected
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("steal=1; Domain=attacker.com"));
        Assert.Equal(0, jar.Count);
    }

    [Fact(DisplayName = "CM-032: Cookie Domain=example.com accepted from sub.example.com")]
    public void Cookie_Domain_SuperDomain_Accepted()
    {
        var jar = new CookieJar();
        // sub.example.com can set a cookie for example.com
        jar.ProcessResponse(Uri("http://sub.example.com/"), ResponseWithCookie("id=1; Domain=example.com"));
        Assert.Equal(1, jar.Count);
    }

    [Fact(DisplayName = "CM-033: Cookie Domain=sub.example.com from example.com is rejected")]
    public void Cookie_Domain_SubDomain_From_Parent_Is_Rejected()
    {
        var jar = new CookieJar();
        // example.com cannot set a cookie for sub.example.com via Domain attr (not a parent)
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("id=1; Domain=sub.example.com"));
        Assert.Equal(0, jar.Count);
    }

    // ── CM-034–CM-038: IP address and IP domain matching ──────────────────────

    [Fact(DisplayName = "CM-034: Cookie from IP address is host-only")]
    public void Cookie_From_Ip_Address_Is_HostOnly()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://192.168.1.1/"), ResponseWithCookie("id=1"));

        var req = new HttpRequestMessage(HttpMethod.Get, "http://192.168.1.1/");
        jar.AddCookiesToRequest(Uri("http://192.168.1.1/"), ref req);

        Assert.True(req.Headers.Contains("Cookie"));
    }

    [Fact(DisplayName = "CM-035: Domain cookie is not matched to IP address host")]
    public void Domain_Cookie_Not_Matched_To_Ip_Address()
    {
        // DomainMatches with IP address request host should return false for domain cookies
        Assert.False(CookieJar.DomainMatches("example.com", false, "192.168.1.1"));
    }

    // ── CM-036–CM-038: DomainMatches unit tests ────────────────────────────────

    [Theory(DisplayName = "CM-036: DomainMatches returns correct result for various combinations")]
    [InlineData("example.com", true, "example.com", true)]
    [InlineData("example.com", true, "sub.example.com", false)]
    [InlineData("example.com", false, "example.com", true)]
    [InlineData("example.com", false, "sub.example.com", true)]
    [InlineData("example.com", false, "notexample.com", false)]
    [InlineData("example.com", false, "other.com", false)]
    [InlineData("example.com", false, "192.168.1.1", false)]
    public void DomainMatches_Correct_Result(string cookieDomain, bool isHostOnly, string requestHost, bool expected)
    {
        Assert.Equal(expected, CookieJar.DomainMatches(cookieDomain, isHostOnly, requestHost));
    }

    // ── CM-037–CM-038: PathMatches unit tests ─────────────────────────────────

    [Theory(DisplayName = "CM-037: PathMatches returns correct result for various combinations")]
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
    public void PathMatches_Correct_Result(string cookiePath, string requestPath, bool expected)
    {
        Assert.Equal(expected, CookieJar.PathMatches(cookiePath, requestPath));
    }

    [Fact(DisplayName = "CM-038: Cookies sorted by path length (longer first) in Cookie header")]
    public void Cookies_Sorted_By_Path_Length_Longer_First()
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

    // ── CM-039–CM-042: Cross-origin redirect cookie re-evaluation ─────────────

    [Fact(DisplayName = "CM-039: Cookie jar evaluates cookies for new URI on redirect")]
    public void Cookie_Jar_Evaluates_For_Redirect_URI()
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

    [Fact(DisplayName = "CM-040: No cookies sent when jar has no matching cookies")]
    public void No_Cookies_Sent_When_No_Match()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("id=1"));

        var req = new HttpRequestMessage(HttpMethod.Get, "http://other.com/");
        jar.AddCookiesToRequest(Uri("http://other.com/"), ref req);

        Assert.False(req.Headers.Contains("Cookie"));
    }

    // ── CM-041–CM-042: Expires date formats ──────────────────────────────────

    [Theory(DisplayName = "CM-041: Various Expires date formats are parsed correctly")]
    [InlineData("Thu, 01 Jan 2099 00:00:00 GMT")]
    [InlineData("Thu, 01-Jan-2099 00:00:00 GMT")]
    public void Various_Expires_Formats_Are_Parsed(string expiresValue)
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie($"x=1; Expires={expiresValue}"));
        Assert.Equal(1, jar.Count);

        var req = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        jar.AddCookiesToRequest(Uri("http://example.com/"), ref req);
        Assert.True(req.Headers.Contains("Cookie"));
    }

    [Fact(DisplayName = "CM-042: Cookie with unrecognized Expires format is treated as session cookie")]
    public void Unrecognized_Expires_Format_Treated_As_Session_Cookie()
    {
        // If Expires can't be parsed, the cookie should still be stored as session cookie
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("x=1; Expires=garbage-date"));
        Assert.Equal(1, jar.Count);
    }
}