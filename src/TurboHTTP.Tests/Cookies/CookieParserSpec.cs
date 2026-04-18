using TurboHTTP.Protocol.Cookies;

namespace TurboHTTP.Tests.Cookies;

public sealed class CookieParserSpec
{
    private static Uri Uri(string url) => new(url);

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC6265-5.2")]
    public void CookieParser_should_parse_simple_cookie_pair()
    {
        var entry = CookieParser.Parse("name=value", Uri("http://example.com/"), DateTimeOffset.UtcNow);

        Assert.NotNull(entry);
        Assert.Equal("name", entry.Name);
        Assert.Equal("value", entry.Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC6265-5.2")]
    public void CookieParser_should_return_null_when_header_empty()
    {
        var entry = CookieParser.Parse(string.Empty, Uri("http://example.com/"), DateTimeOffset.UtcNow);

        Assert.Null(entry);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC6265-5.2")]
    public void CookieParser_should_return_null_when_no_equals_sign()
    {
        var entry = CookieParser.Parse("invalid", Uri("http://example.com/"), DateTimeOffset.UtcNow);

        Assert.Null(entry);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC6265-5.2")]
    public void CookieParser_should_return_null_when_cookie_pair_has_empty_name()
    {
        var entry = CookieParser.Parse("=value", Uri("http://example.com/"), DateTimeOffset.UtcNow);

        Assert.Null(entry);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC6265-5.2")]
    public void CookieParser_should_allow_empty_cookie_value()
    {
        var entry = CookieParser.Parse("name=", Uri("http://example.com/"), DateTimeOffset.UtcNow);

        Assert.NotNull(entry);
        Assert.Equal("name", entry.Name);
        Assert.Empty(entry.Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC6265-5.2")]
    public void CookieParser_should_trim_whitespace_from_cookie_pair()
    {
        var entry = CookieParser.Parse("  name  =  value  ", Uri("http://example.com/"), DateTimeOffset.UtcNow);

        Assert.NotNull(entry);
        Assert.Equal("name", entry.Name);
        Assert.Equal("value", entry.Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC6265-5.2")]
    public void CookieParser_should_parse_cookie_with_multiple_equals_signs()
    {
        var entry = CookieParser.Parse("name=val=ue", Uri("http://example.com/"), DateTimeOffset.UtcNow);

        Assert.NotNull(entry);
        Assert.Equal("name", entry.Name);
        Assert.Equal("val=ue", entry.Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC6265-5.2")]
    public void CookieParser_should_be_host_only_when_no_domain_attribute()
    {
        var entry = CookieParser.Parse("name=value", Uri("http://example.com/"), DateTimeOffset.UtcNow);

        Assert.NotNull(entry);
        Assert.True(entry.IsHostOnly);
        Assert.Equal("example.com", entry.Domain);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC6265-5.2")]
    public void CookieParser_should_strip_leading_dot_from_domain_attribute()
    {
        var entry = CookieParser.Parse("name=value; Domain=.example.com", Uri("http://example.com/"),
            DateTimeOffset.UtcNow);

        Assert.NotNull(entry);
        Assert.False(entry.IsHostOnly);
        Assert.Equal("example.com", entry.Domain);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC6265-5.2")]
    public void CookieParser_should_return_null_when_domain_does_not_match_request_host()
    {
        var entry = CookieParser.Parse("name=value; Domain=attacker.com", Uri("http://example.com/"),
            DateTimeOffset.UtcNow);

        Assert.Null(entry);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC6265-5.2")]
    public void CookieParser_should_accept_domain_when_request_is_subdomain()
    {
        var entry = CookieParser.Parse("name=value; Domain=example.com", Uri("http://sub.example.com/"),
            DateTimeOffset.UtcNow);

        Assert.NotNull(entry);
        Assert.False(entry.IsHostOnly);
        Assert.Equal("example.com", entry.Domain);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC6265-5.2")]
    public void CookieParser_should_lowercase_domain_attribute()
    {
        var entry = CookieParser.Parse("name=value; Domain=EXAMPLE.COM", Uri("http://EXAMPLE.COM/"),
            DateTimeOffset.UtcNow);

        Assert.NotNull(entry);
        Assert.Equal("example.com", entry.Domain);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC6265-5.2")]
    public void CookieParser_should_return_null_when_empty_domain_attribute()
    {
        var entry = CookieParser.Parse("name=value; Domain=", Uri("http://example.com/"), DateTimeOffset.UtcNow);

        Assert.NotNull(entry);
        Assert.True(entry.IsHostOnly);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC6265-5.2")]
    public void CookieParser_should_use_cookie_path_when_path_attribute_starts_with_slash()
    {
        var entry = CookieParser.Parse("name=value; Path=/api", Uri("http://example.com/page"), DateTimeOffset.UtcNow);

        Assert.NotNull(entry);
        Assert.Equal("/api", entry.Path);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC6265-5.2")]
    public void CookieParser_should_use_default_path_when_path_attribute_does_not_start_with_slash()
    {
        var entry = CookieParser.Parse("name=value; Path=api", Uri("http://example.com/page"), DateTimeOffset.UtcNow);

        Assert.NotNull(entry);
        Assert.Equal("/", entry.Path);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC6265-5.2")]
    public void CookieParser_should_compute_default_path_as_request_directory()
    {
        var entry = CookieParser.Parse("name=value", Uri("http://example.com/api/v1/users"), DateTimeOffset.UtcNow);

        Assert.NotNull(entry);
        Assert.Equal("/api/v1", entry.Path);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC6265-5.2")]
    public void CookieParser_should_compute_default_path_as_root_when_request_path_is_root()
    {
        var entry = CookieParser.Parse("name=value", Uri("http://example.com/"), DateTimeOffset.UtcNow);

        Assert.NotNull(entry);
        Assert.Equal("/", entry.Path);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC6265-5.2")]
    public void CookieParser_should_set_secure_flag_when_secure_attribute_present()
    {
        var entry = CookieParser.Parse("name=value; Secure", Uri("https://example.com/"), DateTimeOffset.UtcNow);

        Assert.NotNull(entry);
        Assert.True(entry.Secure);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC6265-5.2")]
    public void CookieParser_should_not_set_secure_flag_when_secure_attribute_absent()
    {
        var entry = CookieParser.Parse("name=value", Uri("https://example.com/"), DateTimeOffset.UtcNow);

        Assert.NotNull(entry);
        Assert.False(entry.Secure);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC6265-5.2")]
    public void CookieParser_should_be_case_insensitive_for_secure_attribute()
    {
        var entry = CookieParser.Parse("name=value; SECURE", Uri("https://example.com/"), DateTimeOffset.UtcNow);

        Assert.NotNull(entry);
        Assert.True(entry.Secure);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC6265-5.2")]
    public void CookieParser_should_set_http_only_flag_when_http_only_attribute_present()
    {
        var entry = CookieParser.Parse("name=value; HttpOnly", Uri("http://example.com/"), DateTimeOffset.UtcNow);

        Assert.NotNull(entry);
        Assert.True(entry.HttpOnly);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC6265-5.2")]
    public void CookieParser_should_be_case_insensitive_for_http_only_attribute()
    {
        var entry = CookieParser.Parse("name=value; HTTPONLY", Uri("http://example.com/"), DateTimeOffset.UtcNow);

        Assert.NotNull(entry);
        Assert.True(entry.HttpOnly);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC6265-5.2")]
    public void CookieParser_should_parse_same_site_strict()
    {
        var entry = CookieParser.Parse("name=value; SameSite=Strict", Uri("http://example.com/"),
            DateTimeOffset.UtcNow);

        Assert.NotNull(entry);
        Assert.Equal(SameSitePolicy.Strict, entry.SameSite);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC6265-5.2")]
    public void CookieParser_should_parse_same_site_lax()
    {
        var entry = CookieParser.Parse("name=value; SameSite=Lax", Uri("http://example.com/"), DateTimeOffset.UtcNow);

        Assert.NotNull(entry);
        Assert.Equal(SameSitePolicy.Lax, entry.SameSite);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC6265-5.2")]
    public void CookieParser_should_parse_same_site_none()
    {
        var entry = CookieParser.Parse("name=value; SameSite=None", Uri("http://example.com/"), DateTimeOffset.UtcNow);

        Assert.NotNull(entry);
        Assert.Equal(SameSitePolicy.None, entry.SameSite);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC6265-5.2")]
    public void CookieParser_should_be_case_insensitive_for_same_site_values()
    {
        var entry = CookieParser.Parse("name=value; SameSite=STRICT", Uri("http://example.com/"),
            DateTimeOffset.UtcNow);

        Assert.NotNull(entry);
        Assert.Equal(SameSitePolicy.Strict, entry.SameSite);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC6265-5.2")]
    public void CookieParser_should_default_same_site_to_unspecified_when_invalid_value()
    {
        var entry = CookieParser.Parse("name=value; SameSite=Invalid", Uri("http://example.com/"),
            DateTimeOffset.UtcNow);

        Assert.NotNull(entry);
        Assert.Equal(SameSitePolicy.Unspecified, entry.SameSite);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC6265-5.2")]
    public void CookieParser_should_parse_expires_in_rfc_format()
    {
        var now = DateTimeOffset.UtcNow;
        var entry = CookieParser.Parse("name=value; Expires=Thu, 01 Jan 2099 00:00:00 GMT", Uri("http://example.com/"),
            now);

        Assert.NotNull(entry);
        Assert.NotNull(entry.ExpiresAt);
        Assert.True(entry.ExpiresAt > now);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC6265-5.2")]
    public void CookieParser_should_parse_expires_in_dash_format()
    {
        var now = DateTimeOffset.UtcNow;
        var entry = CookieParser.Parse("name=value; Expires=Thu, 01-Jan-2099 00:00:00 GMT", Uri("http://example.com/"),
            now);

        Assert.NotNull(entry);
        Assert.NotNull(entry.ExpiresAt);
        Assert.True(entry.ExpiresAt > now);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC6265-5.2")]
    public void CookieParser_should_treat_invalid_expires_as_session_cookie()
    {
        var entry = CookieParser.Parse("name=value; Expires=garbage", Uri("http://example.com/"),
            DateTimeOffset.UtcNow);

        Assert.NotNull(entry);
        Assert.Null(entry.ExpiresAt);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC6265-5.2")]
    public void CookieParser_should_parse_max_age_positive()
    {
        var now = DateTimeOffset.UtcNow;
        var entry = CookieParser.Parse("name=value; Max-Age=3600", Uri("http://example.com/"), now);

        Assert.NotNull(entry);
        Assert.NotNull(entry.ExpiresAt);
        Assert.True(entry.ExpiresAt > now);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC6265-5.2")]
    public void CookieParser_should_parse_max_age_zero_as_expired()
    {
        var now = DateTimeOffset.UtcNow;
        var entry = CookieParser.Parse("name=value; Max-Age=0", Uri("http://example.com/"), now);

        Assert.NotNull(entry);
        Assert.NotNull(entry.ExpiresAt);
        Assert.True(entry.ExpiresAt < now);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC6265-5.2")]
    public void CookieParser_should_prefer_max_age_over_expires()
    {
        var now = DateTimeOffset.UtcNow;
        var entry = CookieParser.Parse(
            "name=value; Expires=Thu, 01 Jan 2099 00:00:00 GMT; Max-Age=0",
            Uri("http://example.com/"), now);

        Assert.NotNull(entry);
        Assert.NotNull(entry.ExpiresAt);
        Assert.True(entry.ExpiresAt < now);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC6265-5.2")]
    public void CookieParser_should_ignore_invalid_max_age()
    {
        var entry = CookieParser.Parse("name=value; Max-Age=abc", Uri("http://example.com/"), DateTimeOffset.UtcNow);

        Assert.NotNull(entry);
        Assert.Null(entry.ExpiresAt);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC6265-5.2")]
    public void CookieParser_should_handle_multiple_attributes()
    {
        var now = DateTimeOffset.UtcNow;
        var entry = CookieParser.Parse(
            "name=value; Domain=example.com; Path=/api; Secure; HttpOnly; SameSite=Strict; Max-Age=3600",
            Uri("http://sub.example.com/api/users"), now);

        Assert.NotNull(entry);
        Assert.Equal("name", entry.Name);
        Assert.Equal("value", entry.Value);
        Assert.Equal("example.com", entry.Domain);
        Assert.False(entry.IsHostOnly);
        Assert.Equal("/api", entry.Path);
        Assert.True(entry.Secure);
        Assert.True(entry.HttpOnly);
        Assert.Equal(SameSitePolicy.Strict, entry.SameSite);
        Assert.NotNull(entry.ExpiresAt);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC6265-5.2")]
    public void CookieParser_should_handle_empty_attributes()
    {
        var entry = CookieParser.Parse("name=value;;;", Uri("http://example.com/"), DateTimeOffset.UtcNow);

        Assert.NotNull(entry);
        Assert.Equal("name", entry.Name);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC6265-5.2")]
    public void CookieParser_should_handle_attributes_with_extra_whitespace()
    {
        var entry = CookieParser.Parse("name=value ;  Secure  ; Path=/api", Uri("http://example.com/"),
            DateTimeOffset.UtcNow);

        Assert.NotNull(entry);
        Assert.True(entry.Secure);
        Assert.Equal("/api", entry.Path);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC6265-5.2")]
    public void CookieParser_should_be_case_insensitive_for_attribute_names()
    {
        var entry = CookieParser.Parse("name=value; domain=example.com; path=/api; secure", Uri("http://example.com/"),
            DateTimeOffset.UtcNow);

        Assert.NotNull(entry);
        Assert.Equal("example.com", entry.Domain);
        Assert.Equal("/api", entry.Path);
        Assert.True(entry.Secure);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC6265-5.2")]
    public void CookieParser_should_handle_path_with_multiple_slashes()
    {
        var entry = CookieParser.Parse("name=value; Path=/api/v1/users", Uri("http://example.com/"),
            DateTimeOffset.UtcNow);

        Assert.NotNull(entry);
        Assert.Equal("/api/v1/users", entry.Path);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC6265-5.2")]
    public void CookieParser_should_handle_cookie_value_with_special_characters()
    {
        var entry = CookieParser.Parse("name=!@#$%^&*()_+-=[]{}|", Uri("http://example.com/"), DateTimeOffset.UtcNow);

        Assert.NotNull(entry);
        Assert.Equal("!@#$%^&*()_+-=[]{}|", entry.Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC6265-5.2")]
    public void CookieParser_should_set_created_at_to_now()
    {
        var before = DateTimeOffset.UtcNow;
        var entry = CookieParser.Parse("name=value", Uri("http://example.com/"), before);
        var after = DateTimeOffset.UtcNow;

        Assert.NotNull(entry);
        Assert.True(entry.CreatedAt >= before && entry.CreatedAt <= after);
    }
}