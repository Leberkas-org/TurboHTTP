using TurboHTTP.Context.Adapters;

namespace TurboHTTP.Tests.Server.Context;

public sealed class TurboRequestCookieCollectionSpec
{
    [Fact(Timeout = 5000)]
    public void Cookie_should_parse_single_cookie()
    {
        var cookies = new TurboRequestCookieCollection("session=abc123");
        Assert.Equal("abc123", cookies["session"]);
    }

    [Fact(Timeout = 5000)]
    public void Cookie_should_parse_multiple_cookies()
    {
        var cookies = new TurboRequestCookieCollection("session=abc123; theme=dark");
        Assert.Equal("abc123", cookies["session"]);
        Assert.Equal("dark", cookies["theme"]);
    }

    [Fact(Timeout = 5000)]
    public void Cookie_should_return_null_for_missing_key()
    {
        var cookies = new TurboRequestCookieCollection("session=abc123");
        Assert.Null(cookies["missing"]);
    }

    [Fact(Timeout = 5000)]
    public void Cookie_should_handle_empty_header()
    {
        var cookies = new TurboRequestCookieCollection(null);
        Assert.Equal(0, cookies.Count);
    }

    [Fact(Timeout = 5000)]
    public void Cookie_should_enumerate_all_cookies()
    {
        var cookies = new TurboRequestCookieCollection("a=1; b=2; c=3");
        Assert.Equal(3, cookies.Count);
    }

    [Fact(Timeout = 5000)]
    public void Cookie_should_contain_key()
    {
        var cookies = new TurboRequestCookieCollection("session=abc123");
        Assert.True(cookies.ContainsKey("session"));
        Assert.False(cookies.ContainsKey("missing"));
    }
}
