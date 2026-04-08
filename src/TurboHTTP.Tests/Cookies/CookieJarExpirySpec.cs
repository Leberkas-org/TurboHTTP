using System.Net;
using TurboHTTP.Protocol.Cookies;

namespace TurboHTTP.Tests.Cookies;

/// <summary>
/// RFC 6265 — Cookie expiry, replacement, SameSite, domain security, and date handling tests.
/// Covers: Max-Age/Expires handling, cookie replacement, Clear(), SameSite attributes,
/// domain security validation, IP address cookies, DomainMatches/PathMatches helpers,
/// cookie sorting, redirect behaviour, and Expires date format parsing.
/// </summary>
/// <remarks>
/// Class under test: <see cref="CookieJar"/>.
/// RFC 6265 §5: Cookie processing model — expiry, deletion, and filtering.
/// </remarks>
public sealed class CookieJarExpirySpec
{
    private static Uri Uri(string url) => new(url);

    private static HttpResponseMessage ResponseWithCookie(string setCookie)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.TryAddWithoutValidation("Set-Cookie", setCookie);
        return response;
    }


    [Trait("RFC", "RFC6265-5.3")]
    [Fact]
    public void CookieJar_should_not_send_cookie_when_cookie_is_expired()
    {
        var jar = new CookieJar();
        // Expires in the distant past
        jar.ProcessResponse(Uri("http://example.com/"),
            ResponseWithCookie("old=x; Expires=Thu, 01 Jan 1970 00:00:00 GMT"));

        var req = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        jar.AddCookiesToRequest(Uri("http://example.com/"), ref req);

        Assert.False(req.Headers.Contains("Cookie"));
    }

    [Trait("RFC", "RFC6265-5.3")]
    [Fact]
    public void CookieJar_should_send_cookie_when_expires_in_future()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/"),
            ResponseWithCookie("future=x; Expires=Thu, 01 Jan 2099 00:00:00 GMT"));

        var req = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        jar.AddCookiesToRequest(Uri("http://example.com/"), ref req);

        Assert.True(req.Headers.Contains("Cookie"));
    }

    [Trait("RFC", "RFC6265-5.3")]
    [Fact]
    public void CookieJar_should_delete_cookie_when_max_age_is_zero()
    {
        var jar = new CookieJar();
        // First: add the cookie
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("session=abc"));
        Assert.Equal(1, jar.Count);

        // Then: delete it with Max-Age=0
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("session=abc; Max-Age=0"));
        Assert.Equal(0, jar.Count);
    }

    [Trait("RFC", "RFC6265-5.3")]
    [Fact]
    public void CookieJar_should_prefer_max_age_when_both_max_age_and_expires_present()
    {
        var jar = new CookieJar();
        // Expires says far future, but Max-Age=0 should win and delete
        jar.ProcessResponse(Uri("http://example.com/"),
            ResponseWithCookie("x=1; Expires=Thu, 01 Jan 2099 00:00:00 GMT; Max-Age=0"));

        Assert.Equal(0, jar.Count);
    }

    [Trait("RFC", "RFC6265-5.3")]
    [Fact]
    public void CookieJar_should_set_future_expiry_when_max_age_is_positive()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("x=1; Max-Age=3600"));
        Assert.Equal(1, jar.Count);

        var req = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        jar.AddCookiesToRequest(Uri("http://example.com/"), ref req);

        Assert.True(req.Headers.Contains("Cookie"));
    }


    [Trait("RFC", "RFC6265-5.3")]
    [Fact]
    public void CookieJar_should_replace_cookie_when_same_name_domain_path()
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

    [Trait("RFC", "RFC6265-5.3")]
    [Fact]
    public void CookieJar_should_allow_coexistence_when_same_name_different_paths()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/api/v1"), ResponseWithCookie("x=1; Path=/api/v1"));
        jar.ProcessResponse(Uri("http://example.com/api/v2"), ResponseWithCookie("x=2; Path=/api/v2"));

        Assert.Equal(2, jar.Count);
    }

    [Trait("RFC", "RFC6265-5.3")]
    [Fact]
    public void CookieJar_should_remove_all_cookies_when_clear_called()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("a=1"));
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("b=2"));

        jar.Clear();

        Assert.Equal(0, jar.Count);
    }


    [Trait("RFC", "RFC6265-5.3")]
    [Fact]
    public void CookieJar_should_store_cookie_when_same_site_strict()
    {
        // We verify the cookie is stored (enforcement is caller's responsibility)
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("id=1; SameSite=Strict"));
        Assert.Equal(1, jar.Count);
    }

    [Trait("RFC", "RFC6265-5.3")]
    [Fact]
    public void CookieJar_should_store_cookie_when_same_site_lax()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("id=1; SameSite=Lax"));
        Assert.Equal(1, jar.Count);
    }


    [Trait("RFC", "RFC6265-5.3")]
    [Fact]
    public void CookieJar_should_reject_cookie_when_domain_for_unrelated_host()
    {
        var jar = new CookieJar();
        // Server at example.com tries to set a cookie for attacker.com — must be rejected
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("steal=1; Domain=attacker.com"));
        Assert.Equal(0, jar.Count);
    }

    [Trait("RFC", "RFC6265-5.3")]
    [Fact]
    public void CookieJar_should_accept_cookie_when_domain_is_super_domain()
    {
        var jar = new CookieJar();
        // sub.example.com can set a cookie for example.com
        jar.ProcessResponse(Uri("http://sub.example.com/"), ResponseWithCookie("id=1; Domain=example.com"));
        Assert.Equal(1, jar.Count);
    }

    [Trait("RFC", "RFC6265-5.3")]
    [Fact]
    public void CookieJar_should_reject_cookie_when_sub_domain_set_by_parent()
    {
        var jar = new CookieJar();
        // example.com cannot set a cookie for sub.example.com via Domain attr (not a parent)
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("id=1; Domain=sub.example.com"));
        Assert.Equal(0, jar.Count);
    }


    [Trait("RFC", "RFC6265-5.1.3")]
    [Fact]
    public void CookieJar_should_be_host_only_when_cookie_from_ip_address()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://192.168.1.1/"), ResponseWithCookie("id=1"));

        var req = new HttpRequestMessage(HttpMethod.Get, "http://192.168.1.1/");
        jar.AddCookiesToRequest(Uri("http://192.168.1.1/"), ref req);

        Assert.True(req.Headers.Contains("Cookie"));
    }

    [Trait("RFC", "RFC6265-5.1.3")]
    [Fact]
    public void CookieJar_should_not_match_ip_address_when_domain_cookie()
    {
        // DomainMatches with IP address request host should return false for domain cookies
        Assert.False(CookieJar.DomainMatches("example.com", false, "192.168.1.1"));
    }


    [Trait("RFC", "RFC6265-5.1.3")]
    [Theory]
    [InlineData("example.com", true, "example.com", true)]
    [InlineData("example.com", true, "sub.example.com", false)]
    [InlineData("example.com", false, "example.com", true)]
    [InlineData("example.com", false, "sub.example.com", true)]
    [InlineData("example.com", false, "notexample.com", false)]
    [InlineData("example.com", false, "other.com", false)]
    [InlineData("example.com", false, "192.168.1.1", false)]
    public void CookieJar_should_return_correct_domain_match_result(string cookieDomain, bool isHostOnly, string requestHost, bool expected)
    {
        Assert.Equal(expected, CookieJar.DomainMatches(cookieDomain, isHostOnly, requestHost));
    }


    [Trait("RFC", "RFC6265-5.1.4")]
    [Theory]
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
    public void CookieJar_should_return_correct_path_match_result(string cookiePath, string requestPath, bool expected)
    {
        Assert.Equal(expected, CookieJar.PathMatches(cookiePath, requestPath));
    }

    [Trait("RFC", "RFC6265-5.1.4")]
    [Fact]
    public void CookieJar_should_sort_cookies_by_path_length_longer_first_when_building_cookie_header()
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


    [Trait("RFC", "RFC6265-5.3")]
    [Fact]
    public void CookieJar_should_evaluate_cookies_for_new_uri_when_redirecting()
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

    [Trait("RFC", "RFC6265-5.3")]
    [Fact]
    public void CookieJar_should_not_send_cookies_when_no_cookies_match_uri()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("id=1"));

        var req = new HttpRequestMessage(HttpMethod.Get, "http://other.com/");
        jar.AddCookiesToRequest(Uri("http://other.com/"), ref req);

        Assert.False(req.Headers.Contains("Cookie"));
    }


    [Trait("RFC", "RFC6265-5.3")]
    [Theory]
    [InlineData("Thu, 01 Jan 2099 00:00:00 GMT")]
    [InlineData("Thu, 01-Jan-2099 00:00:00 GMT")]
    public void CookieJar_should_parse_expires_when_various_date_formats(string expiresValue)
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie($"x=1; Expires={expiresValue}"));
        Assert.Equal(1, jar.Count);

        var req = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        jar.AddCookiesToRequest(Uri("http://example.com/"), ref req);
        Assert.True(req.Headers.Contains("Cookie"));
    }

    [Trait("RFC", "RFC6265-5.3")]
    [Fact]
    public void CookieJar_should_treat_as_session_cookie_when_expires_format_unrecognized()
    {
        // If Expires can't be parsed, the cookie should still be stored as session cookie
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("x=1; Expires=garbage-date"));
        Assert.Equal(1, jar.Count);
    }
}
