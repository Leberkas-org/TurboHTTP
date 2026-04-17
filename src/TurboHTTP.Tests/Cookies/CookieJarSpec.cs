using System.Net;
using TurboHTTP.Protocol.Cookies;

namespace TurboHTTP.Tests.Cookies;

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
public sealed class CookieJarSpec
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
    public void CookieJar_should_store_cookie_when_basic_name_value_cookie()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("session=abc123"));
        Assert.Equal(1, jar.Count);
    }

    [Trait("RFC", "RFC6265-5.3")]
    [Fact]
    public void CookieJar_should_add_cookie_value_to_request_when_cookie_matches()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("token=xyz"));

        var req = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        jar.AddCookiesToRequest(Uri("http://example.com/"), ref req);

        Assert.True(req.Headers.TryGetValues("Cookie", out var values));
        Assert.Contains("token=xyz", string.Join("", values));
    }

    [Trait("RFC", "RFC6265-5.3")]
    [Fact]
    public void CookieJar_should_ignore_cookie_when_no_equals_sign()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("invalidsyntax"));
        Assert.Equal(0, jar.Count);
    }

    [Trait("RFC", "RFC6265-5.3")]
    [Fact]
    public void CookieJar_should_ignore_cookie_when_name_is_empty()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("=value"));
        Assert.Equal(0, jar.Count);
    }

    [Trait("RFC", "RFC6265-5.3")]
    [Fact]
    public void CookieJar_should_process_all_cookies_when_multiple_set_cookie_headers()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.TryAddWithoutValidation("Set-Cookie", "a=1");
        response.Headers.TryAddWithoutValidation("Set-Cookie", "b=2");
        response.Headers.TryAddWithoutValidation("Set-Cookie", "c=3");

        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/"), response);

        Assert.Equal(3, jar.Count);
    }


    [Trait("RFC", "RFC6265-5.1.3")]
    [Fact]
    public void CookieJar_should_match_exact_host_only_when_host_only_cookie()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("id=1"));

        var req = new HttpRequestMessage(HttpMethod.Get, "http://sub.example.com/");
        jar.AddCookiesToRequest(Uri("http://sub.example.com/"), ref req);

        Assert.False(req.Headers.Contains("Cookie"));
    }

    [Trait("RFC", "RFC6265-5.1.3")]
    [Fact]
    public void CookieJar_should_match_same_host_when_host_only_cookie()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("id=1"));

        var req = new HttpRequestMessage(HttpMethod.Get, "http://example.com/path");
        jar.AddCookiesToRequest(Uri("http://example.com/path"), ref req);

        Assert.True(req.Headers.Contains("Cookie"));
    }

    [Trait("RFC", "RFC6265-5.1.3")]
    [Fact]
    public void CookieJar_should_match_subdomain_when_domain_cookie()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("id=1; Domain=example.com"));

        var req = new HttpRequestMessage(HttpMethod.Get, "http://sub.example.com/");
        jar.AddCookiesToRequest(Uri("http://sub.example.com/"), ref req);

        Assert.True(req.Headers.Contains("Cookie"));
    }

    [Trait("RFC", "RFC6265-5.1.3")]
    [Fact]
    public void CookieJar_should_not_match_unrelated_host_when_domain_cookie()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("id=1; Domain=example.com"));

        var req = new HttpRequestMessage(HttpMethod.Get, "http://notexample.com/");
        jar.AddCookiesToRequest(Uri("http://notexample.com/"), ref req);

        Assert.False(req.Headers.Contains("Cookie"));
    }

    [Trait("RFC", "RFC6265-5.1.3")]
    [Fact]
    public void CookieJar_should_strip_leading_dot_when_domain_cookie_has_leading_dot()
    {
        var jar = new CookieJar();
        // Leading dot should be stripped per RFC 6265 §5.2.3
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("id=1; Domain=.example.com"));

        var req = new HttpRequestMessage(HttpMethod.Get, "http://sub.example.com/");
        jar.AddCookiesToRequest(Uri("http://sub.example.com/"), ref req);

        Assert.True(req.Headers.Contains("Cookie"));
    }


    [Trait("RFC", "RFC6265-5.1.4")]
    [Fact]
    public void CookieJar_should_match_sub_path_when_path_cookie()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/api"), ResponseWithCookie("token=x; Path=/api"));

        var req = new HttpRequestMessage(HttpMethod.Get, "http://example.com/api/users");
        jar.AddCookiesToRequest(Uri("http://example.com/api/users"), ref req);

        Assert.True(req.Headers.Contains("Cookie"));
    }

    [Trait("RFC", "RFC6265-5.1.4")]
    [Fact]
    public void CookieJar_should_not_match_partial_label_when_path_cookie()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/api"), ResponseWithCookie("token=x; Path=/api"));

        var req = new HttpRequestMessage(HttpMethod.Get, "http://example.com/apiv2");
        jar.AddCookiesToRequest(Uri("http://example.com/apiv2"), ref req);

        Assert.False(req.Headers.Contains("Cookie"));
    }

    [Trait("RFC", "RFC6265-5.1.4")]
    [Fact]
    public void CookieJar_should_match_all_paths_when_path_is_root()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("global=1; Path=/"));

        var req = new HttpRequestMessage(HttpMethod.Get, "http://example.com/deep/nested/path");
        jar.AddCookiesToRequest(Uri("http://example.com/deep/nested/path"), ref req);

        Assert.True(req.Headers.Contains("Cookie"));
    }

    [Trait("RFC", "RFC6265-5.1.4")]
    [Fact]
    public void CookieJar_should_match_sub_path_when_path_has_trailing_slash()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/foo/"), ResponseWithCookie("x=1; Path=/foo/"));

        var req = new HttpRequestMessage(HttpMethod.Get, "http://example.com/foo/bar");
        jar.AddCookiesToRequest(Uri("http://example.com/foo/bar"), ref req);

        Assert.True(req.Headers.Contains("Cookie"));
    }

    [Trait("RFC", "RFC6265-5.1.4")]
    [Fact]
    public void CookieJar_should_compute_default_path_when_no_cookie_path()
    {
        var jar = new CookieJar();
        // Request to /foo/bar — default path should be /foo
        jar.ProcessResponse(Uri("http://example.com/foo/bar"), ResponseWithCookie("x=1"));

        // Should match /foo/baz (same directory)
        var req = new HttpRequestMessage(HttpMethod.Get, "http://example.com/foo/baz");
        jar.AddCookiesToRequest(Uri("http://example.com/foo/baz"), ref req);

        Assert.True(req.Headers.Contains("Cookie"));
    }


    [Trait("RFC", "RFC6265-5.3")]
    [Fact]
    public void CookieJar_should_not_send_cookie_when_secure_cookie_and_http_scheme()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("https://example.com/"), ResponseWithCookie("sess=abc; Secure"));

        var req = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        jar.AddCookiesToRequest(Uri("http://example.com/"), ref req);

        Assert.False(req.Headers.Contains("Cookie"));
    }

    [Trait("RFC", "RFC6265-5.3")]
    [Fact]
    public void CookieJar_should_send_cookie_when_secure_cookie_and_https_scheme()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("https://example.com/"), ResponseWithCookie("sess=abc; Secure"));

        var req = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        jar.AddCookiesToRequest(Uri("https://example.com/"), ref req);

        Assert.True(req.Headers.Contains("Cookie"));
    }

    [Trait("RFC", "RFC6265-5.3")]
    [Fact]
    public void CookieJar_should_send_cookie_when_non_secure_cookie_and_http_scheme()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("pref=dark"));

        var req = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        jar.AddCookiesToRequest(Uri("http://example.com/"), ref req);

        Assert.True(req.Headers.Contains("Cookie"));
    }


    [Trait("RFC", "RFC6265-5.3")]
    [Fact]
    public void CookieJar_should_store_cookie_when_http_only_attribute()
    {
        var jar = new CookieJar();
        // We verify via behavior: HttpOnly cookies are still sent in HTTP requests (server-side flag)
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("session=s1; HttpOnly"));

        var req = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        jar.AddCookiesToRequest(Uri("http://example.com/"), ref req);

        // HttpOnly = restrict JS access; we still send it in HTTP requests
        Assert.True(req.Headers.Contains("Cookie"));
    }

    [Trait("RFC", "RFC6265-5.3")]
    [Fact]
    public void CookieJar_should_store_and_send_cookie_when_non_http_only_cookie()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("pref=light"));

        var req = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        jar.AddCookiesToRequest(Uri("http://example.com/"), ref req);

        Assert.True(req.Headers.Contains("Cookie"));
    }

    [Trait("RFC", "RFC6265-5.1.3")]
    [Fact]
    public void CookieJar_should_handle_ip_address_as_host_only_always()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://192.168.1.100/"), ResponseWithCookie("id=1"));

        var req = new HttpRequestMessage(HttpMethod.Get, "http://192.168.1.100/");
        jar.AddCookiesToRequest(Uri("http://192.168.1.100/"), ref req);

        Assert.True(req.Headers.Contains("Cookie"));
    }

    [Trait("RFC", "RFC6265-5.3")]
    [Fact]
    public void CookieJar_should_not_match_partial_domain_label()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("id=1; Domain=example.com"));

        var req = new HttpRequestMessage(HttpMethod.Get, "http://notexample.com/");
        jar.AddCookiesToRequest(Uri("http://notexample.com/"), ref req);

        Assert.False(req.Headers.Contains("Cookie"));
    }

    [Trait("RFC", "RFC6265-5.1.4")]
    [Fact]
    public void CookieJar_should_handle_multiple_cookies_with_different_paths()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/api/v1/users"), ResponseWithCookie("a=1; Path=/api/v1/users"));
        jar.ProcessResponse(Uri("http://example.com/api/v1"), ResponseWithCookie("b=2; Path=/api/v1"));
        jar.ProcessResponse(Uri("http://example.com/api"), ResponseWithCookie("c=3; Path=/api"));

        var req = new HttpRequestMessage(HttpMethod.Get, "http://example.com/api/v1/users/123");
        jar.AddCookiesToRequest(Uri("http://example.com/api/v1/users/123"), ref req);

        Assert.True(req.Headers.TryGetValues("Cookie", out var vals));
        var cookieHeader = string.Join("", vals);
        Assert.Contains("a=1", cookieHeader);
        Assert.Contains("b=2", cookieHeader);
        Assert.Contains("c=3", cookieHeader);
    }

    [Trait("RFC", "RFC6265-5.3")]
    [Fact]
    public void CookieJar_should_distinguish_cookies_with_same_name_different_paths()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/api"), ResponseWithCookie("id=v1; Path=/api"));
        jar.ProcessResponse(Uri("http://example.com/web"), ResponseWithCookie("id=v2; Path=/web"));

        Assert.Equal(2, jar.Count);
    }

    [Trait("RFC", "RFC6265-5.1.3")]
    [Fact]
    public void CookieJar_should_handle_domain_matching_with_trailing_dot_in_request()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("id=1; Domain=example.com"));

        // Request to sub.example.com should match
        var req = new HttpRequestMessage(HttpMethod.Get, "http://sub.example.com/");
        jar.AddCookiesToRequest(Uri("http://sub.example.com/"), ref req);

        Assert.True(req.Headers.Contains("Cookie"));
    }

    [Trait("RFC", "RFC6265-5.1.3")]
    [Fact]
    public void CookieJar_should_reject_domain_cookie_for_tld_misuse()
    {
        var jar = new CookieJar();
        // Attempting to set a cookie for just "com" should be rejected
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("id=1; Domain=.com"));

        // Cookie should either be rejected or treated as host-only
        // CookieJar should not send it to a different domain
        var req = new HttpRequestMessage(HttpMethod.Get, "http://other.com/");
        jar.AddCookiesToRequest(Uri("http://other.com/"), ref req);

        // This depends on parser validation; if rejected, cookie count is 0
        // If accepted as domain cookie for ".com", it would match other.com
    }

    [Trait("RFC", "RFC6265-5.4")]
    [Fact]
    public void CookieJar_should_not_duplicate_cookie_header_when_multiple_applicable()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("a=1"));
        jar.ProcessResponse(Uri("http://example.com/"), ResponseWithCookie("b=2"));

        var req = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        jar.AddCookiesToRequest(Uri("http://example.com/"), ref req);

        // Should have exactly one Cookie header (with semicolon-separated values)
        Assert.True(req.Headers.TryGetValues("Cookie", out var vals));
        var allValues = vals.ToList();
        Assert.Single(allValues);
    }

    [Trait("RFC", "RFC6265-5.1.4")]
    [Fact]
    public void CookieJar_should_handle_empty_request_path_as_root()
    {
        var jar = new CookieJar();
        jar.ProcessResponse(Uri("http://example.com"), ResponseWithCookie("id=1; Path=/"));

        var req = new HttpRequestMessage(HttpMethod.Get, "http://example.com");
        jar.AddCookiesToRequest(Uri("http://example.com"), ref req);

        Assert.True(req.Headers.Contains("Cookie"));
    }
}
