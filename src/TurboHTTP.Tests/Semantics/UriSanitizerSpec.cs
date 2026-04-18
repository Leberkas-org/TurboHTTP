using TurboHTTP.Protocol.Semantics;

namespace TurboHTTP.Tests.Semantics;

public sealed class UriSanitizerSpec
{
    [Fact(Timeout = 5000)]
    public void FormatAuthority_should_return_host_without_port_when_default_http()
    {
        var uri = new Uri("http://example.com/path");
        var result = UriSanitizer.FormatAuthority(uri);
        Assert.Equal("example.com", result);
    }

    [Fact(Timeout = 5000)]
    public void FormatAuthority_should_return_host_without_port_when_default_https()
    {
        var uri = new Uri("https://example.com/path");
        var result = UriSanitizer.FormatAuthority(uri);
        Assert.Equal("example.com", result);
    }

    [Fact(Timeout = 5000)]
    public void FormatAuthority_should_include_non_default_port()
    {
        var uri = new Uri("http://example.com:8080/path");
        var result = UriSanitizer.FormatAuthority(uri);
        Assert.Equal("example.com:8080", result);
    }

    [Fact(Timeout = 5000)]
    public void FormatAuthority_should_wrap_ipv6_in_brackets()
    {
        var uri = new Uri("http://[::1]/path");
        var result = UriSanitizer.FormatAuthority(uri);
        Assert.Equal("[::1]", result);
    }

    [Fact(Timeout = 5000)]
    public void FormatAuthority_should_include_port_with_ipv6()
    {
        var uri = new Uri("http://[::1]:8080/path");
        var result = UriSanitizer.FormatAuthority(uri);
        Assert.Equal("[::1]:8080", result);
    }

    [Fact(Timeout = 5000)]
    public void FormatAuthority_should_handle_full_ipv6_address()
    {
        var uri = new Uri("http://[2001:db8::1]/path");
        var result = UriSanitizer.FormatAuthority(uri);
        Assert.Equal("[2001:db8::1]", result);
    }

    [Fact(Timeout = 5000)]
    public void FormatAuthorityWithPort_should_always_include_port_for_http()
    {
        var uri = new Uri("http://example.com/path");
        var result = UriSanitizer.FormatAuthorityWithPort(uri);
        Assert.Equal("example.com:80", result);
    }

    [Fact(Timeout = 5000)]
    public void FormatAuthorityWithPort_should_always_include_port_for_https()
    {
        var uri = new Uri("https://example.com/path");
        var result = UriSanitizer.FormatAuthorityWithPort(uri);
        Assert.Equal("example.com:443", result);
    }

    [Fact(Timeout = 5000)]
    public void FormatAuthorityWithPort_should_include_non_default_port()
    {
        var uri = new Uri("http://example.com:8080/path");
        var result = UriSanitizer.FormatAuthorityWithPort(uri);
        Assert.Equal("example.com:8080", result);
    }

    [Fact(Timeout = 5000)]
    public void FormatAuthorityWithPort_should_wrap_ipv6_with_default_port()
    {
        var uri = new Uri("http://[::1]/path");
        var result = UriSanitizer.FormatAuthorityWithPort(uri);
        Assert.Equal("[::1]:80", result);
    }

    [Fact(Timeout = 5000)]
    public void FormatAuthorityWithPort_should_wrap_ipv6_with_custom_port()
    {
        var uri = new Uri("http://[::1]:8080/path");
        var result = UriSanitizer.FormatAuthorityWithPort(uri);
        Assert.Equal("[::1]:8080", result);
    }

    [Fact(Timeout = 5000)]
    public void FormatAuthorityWithPort_should_throw_for_unknown_scheme()
    {
        var uri = new Uri("ftp://example.com/path");
        Assert.Throws<ArgumentException>(() => UriSanitizer.FormatAuthorityWithPort(uri));
    }

    [Fact(Timeout = 5000)]
    public void StripUserInfo_should_remove_username_and_password()
    {
        var uri = new Uri("http://user:password@example.com/path");
        var result = UriSanitizer.StripUserInfo(uri);
        Assert.DoesNotContain("user", result);
        Assert.DoesNotContain("password", result);
        Assert.Contains("example.com", result);
    }

    [Fact(Timeout = 5000)]
    public void StripUserInfo_should_preserve_path_query_fragment()
    {
        var uri = new Uri("http://user:password@example.com/path?query=value#fragment");
        var result = UriSanitizer.StripUserInfo(uri);
        Assert.Contains("/path", result);
        Assert.Contains("query=value", result);
        Assert.Contains("fragment", result);
    }

    [Fact(Timeout = 5000)]
    public void StripUserInfo_should_handle_uri_without_userinfo()
    {
        var uri = new Uri("http://example.com/path");
        var result = UriSanitizer.StripUserInfo(uri);
        Assert.Equal(uri.ToString(), result);
    }

    [Fact(Timeout = 5000)]
    public void FormatAbsoluteWithoutUserInfo_should_remove_userinfo()
    {
        var uri = new Uri("http://user:password@example.com/path");
        var result = UriSanitizer.FormatAbsoluteWithoutUserInfo(uri);
        Assert.DoesNotContain("user", result);
        Assert.DoesNotContain("password", result);
    }

    [Fact(Timeout = 5000)]
    public void FormatAbsoluteWithoutUserInfo_should_remove_fragment()
    {
        var uri = new Uri("http://user:password@example.com/path#fragment");
        var result = UriSanitizer.FormatAbsoluteWithoutUserInfo(uri);
        Assert.DoesNotContain("fragment", result);
    }

    [Fact(Timeout = 5000)]
    public void FormatAbsoluteWithoutUserInfo_should_preserve_scheme_host_port()
    {
        var uri = new Uri("http://user@example.com:8080/path?query=value#fragment");
        var result = UriSanitizer.FormatAbsoluteWithoutUserInfo(uri);
        Assert.StartsWith("http://example.com:8080", result);
    }

    [Fact(Timeout = 5000)]
    public void FormatAbsoluteWithoutUserInfo_should_preserve_query_string()
    {
        var uri = new Uri("https://user:pass@example.com/path?key=value");
        var result = UriSanitizer.FormatAbsoluteWithoutUserInfo(uri);
        Assert.Contains("key=value", result);
    }

    [Fact(Timeout = 5000)]
    public void FormatAbsoluteWithoutUserInfo_should_handle_uri_without_userinfo()
    {
        var uri = new Uri("http://example.com/path");
        var result = UriSanitizer.FormatAbsoluteWithoutUserInfo(uri);
        Assert.Equal("http://example.com/path", result);
    }

    [Fact(Timeout = 5000)]
    public void FormatAbsoluteWithoutUserInfo_should_handle_uri_with_only_username()
    {
        var uri = new Uri("http://user@example.com/path");
        var result = UriSanitizer.FormatAbsoluteWithoutUserInfo(uri);
        Assert.DoesNotContain("user", result);
        Assert.Contains("example.com", result);
    }
}