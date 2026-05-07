using TurboHTTP.Protocol;

namespace TurboHTTP.Tests;

public sealed class WellKnownHeaderValueSpec
{
    [Theory(Timeout = 5000)]
    [InlineData("0", "0")]
    [InlineData("1", "1")]
    [InlineData("br", "br")]
    [InlineData("gzip", "gzip")]
    [InlineData("none", "none")]
    [InlineData("close", "close")]
    [InlineData("bytes", "bytes")]
    [InlineData("public", "public")]
    [InlineData("chunked", "chunked")]
    [InlineData("deflate", "deflate")]
    [InlineData("private", "private")]
    [InlineData("trailer", "trailer")]
    [InlineData("compress", "compress")]
    [InlineData("identity", "identity")]
    [InlineData("no-cache", "no-cache")]
    [InlineData("no-store", "no-store")]
    [InlineData("trailers", "trailers")]
    [InlineData("keep-alive", "keep-alive")]
    [InlineData("max-age=300", "max-age=300")]
    [InlineData("max-age=604800", "max-age=604800")]
    [InlineData("application/json", "application/json")]
    [InlineData("application/octet-stream", "application/octet-stream")]
    public void GetOrCreateHeaderValue_should_intern_well_known_values(string input, string expected)
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes(input);
        var result = WellKnownHeaders.GetOrCreateHeaderValue(bytes);
        Assert.Equal(expected, result);
    }

    [Theory(Timeout = 5000)]
    [InlineData("2")]
    [InlineData("zstd")]
    [InlineData("custom")]
    [InlineData("unknown-value")]
    [InlineData("text/html")]
    [InlineData("text/plain")]
    public void GetOrCreateHeaderValue_should_allocate_for_unknown_values(string input)
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes(input);
        var result = WellKnownHeaders.GetOrCreateHeaderValue(bytes);
        Assert.Equal(input, result);
    }
}

public sealed class WellKnownHeaderNameExtendedSpec
{
    [Theory(Timeout = 5000)]
    [InlineData("Via", "Via")]
    [InlineData("ETag", "ETag")]
    [InlineData("Vary", "Vary")]
    [InlineData("From", "From")]
    [InlineData("Link", "Link")]
    [InlineData("Allow", "Allow")]
    [InlineData("Accept", "Accept")]
    [InlineData("Cookie", "Cookie")]
    [InlineData("Expect", "Expect")]
    [InlineData("Pragma", "Pragma")]
    [InlineData("Server", "Server")]
    [InlineData("Alt-Svc", "Alt-Svc")]
    [InlineData("Expires", "Expires")]
    [InlineData("Referer", "Referer")]
    [InlineData("Trailer", "Trailer")]
    [InlineData("Upgrade", "Upgrade")]
    [InlineData("Warning", "Warning")]
    [InlineData("If-Match", "If-Match")]
    [InlineData("If-Range", "If-Range")]
    [InlineData("Location", "Location")]
    [InlineData("Forwarded", "Forwarded")]
    [InlineData("Keep-Alive", "Keep-Alive")]
    [InlineData("Set-Cookie", "Set-Cookie")]
    [InlineData("User-Agent", "User-Agent")]
    [InlineData("Retry-After", "Retry-After")]
    [InlineData("Set-Cookie2", "Set-Cookie2")]
    [InlineData("Content-Type", "Content-Type")]
    [InlineData("Max-Forwards", "Max-Forwards")]
    [InlineData("X-Request-Id", "X-Request-Id")]
    [InlineData("Cache-Control", "Cache-Control")]
    [InlineData("Content-Range", "Content-Range")]
    [InlineData("Last-Modified", "Last-Modified")]
    [InlineData("If-None-Match", "If-None-Match")]
    [InlineData("Accept-Charset", "Accept-Charset")]
    [InlineData("Accept-Ranges", "Accept-Ranges")]
    [InlineData("Content-Length", "Content-Length")]
    [InlineData("Accept-Encoding", "Accept-Encoding")]
    [InlineData("Accept-Language", "Accept-Language")]
    [InlineData("X-Forwarded-For", "X-Forwarded-For")]
    [InlineData("Content-Encoding", "Content-Encoding")]
    [InlineData("Content-Language", "Content-Language")]
    [InlineData("Content-Location", "Content-Location")]
    [InlineData("WWW-Authenticate", "WWW-Authenticate")]
    [InlineData("If-Modified-Since", "If-Modified-Since")]
    [InlineData("Transfer-Encoding", "Transfer-Encoding")]
    [InlineData("X-Forwarded-Proto", "X-Forwarded-Proto")]
    [InlineData("Proxy-Authenticate", "Proxy-Authenticate")]
    [InlineData("If-Unmodified-Since", "If-Unmodified-Since")]
    [InlineData("Proxy-Authorization", "Proxy-Authorization")]
    [InlineData("Strict-Transport-Security", "Strict-Transport-Security")]
    public void GetOrCreateHeaderName_should_intern_all_known_names(string input, string expected)
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes(input);
        var result = WellKnownHeaders.GetOrCreateHeaderName(bytes);
        Assert.Equal(expected, result);
    }

    [Theory(Timeout = 5000)]
    [InlineData("X")]
    [InlineData("ABC")]
    [InlineData("Nope")]
    [InlineData("XXXXX")]
    [InlineData("Random")]
    [InlineData("Unknown")]
    [InlineData("BadMatch")]
    [InlineData("NotAMatch")]
    [InlineData("SomeHeader")]
    [InlineData("CustomValue")]
    [InlineData("WrongHeader!")]
    [InlineData("NotCacheCtrl")]
    [InlineData("NotContentLen")]
    [InlineData("NotContentEnco")]
    [InlineData("NotTransferEnco")]
    [InlineData("SixteenCharName!")]
    [InlineData("NotTransferEncode")]
    [InlineData("EighteenCharHeader")]
    [InlineData("NineteenCharHeader!")]
    [InlineData("X-Very-Long-Custom-Header-Name")]
    public void GetOrCreateHeaderName_should_allocate_for_unknown_names(string input)
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes(input);
        var result = WellKnownHeaders.GetOrCreateHeaderName(bytes);
        Assert.Equal(input, result);
    }

    [Fact(Timeout = 5000)]
    public void GetOrCreateHeaderName_should_handle_empty_span()
    {
        var result = WellKnownHeaders.GetOrCreateHeaderName([]);
        Assert.Equal("", result);
    }

    [Fact(Timeout = 5000)]
    public void ContainsChunked_should_find_exact_match()
    {
        Assert.True(WellKnownHeaders.ContainsChunked("chunked"u8));
    }

    [Fact(Timeout = 5000)]
    public void ContainsChunked_should_find_case_insensitive()
    {
        Assert.True(WellKnownHeaders.ContainsChunked("Chunked"u8));
        Assert.True(WellKnownHeaders.ContainsChunked("CHUNKED"u8));
    }

    [Fact(Timeout = 5000)]
    public void ContainsChunked_should_find_in_list()
    {
        Assert.True(WellKnownHeaders.ContainsChunked("gzip, chunked"u8));
    }

    [Fact(Timeout = 5000)]
    public void ContainsChunked_should_return_false_for_too_short()
    {
        Assert.False(WellKnownHeaders.ContainsChunked("chunk"u8));
    }

    [Fact(Timeout = 5000)]
    public void ContainsChunked_should_return_false_for_absent()
    {
        Assert.False(WellKnownHeaders.ContainsChunked("gzip, deflate"u8));
    }

    [Fact(Timeout = 5000)]
    public void TrimOws_should_trim_spaces()
    {
        var result = WellKnownHeaders.TrimOws("  hello  "u8);
        Assert.True(result.SequenceEqual("hello"u8));
    }

    [Fact(Timeout = 5000)]
    public void TrimOws_should_trim_tabs()
    {
        var result = WellKnownHeaders.TrimOws("\thello\t"u8);
        Assert.True(result.SequenceEqual("hello"u8));
    }

    [Fact(Timeout = 5000)]
    public void TrimOws_should_handle_empty()
    {
        var result = WellKnownHeaders.TrimOws([]);
        Assert.True(result.IsEmpty);
    }

    [Fact(Timeout = 5000)]
    public void TrimOws_should_handle_all_whitespace()
    {
        var result = WellKnownHeaders.TrimOws("   "u8);
        Assert.True(result.IsEmpty);
    }

    [Fact(Timeout = 5000)]
    public void EqualsIgnoreCase_should_match_same_case()
    {
        Assert.True(WellKnownHeaders.EqualsIgnoreCase("Host"u8, "Host"u8));
    }

    [Fact(Timeout = 5000)]
    public void EqualsIgnoreCase_should_match_different_case()
    {
        Assert.True(WellKnownHeaders.EqualsIgnoreCase("HOST"u8, "host"u8));
    }

    [Fact(Timeout = 5000)]
    public void EqualsIgnoreCase_should_not_match_different_length()
    {
        Assert.False(WellKnownHeaders.EqualsIgnoreCase("Host"u8, "Hosts"u8));
    }

    [Fact(Timeout = 5000)]
    public void EqualsIgnoreCase_should_not_match_different_content()
    {
        Assert.False(WellKnownHeaders.EqualsIgnoreCase("Host"u8, "Hose"u8));
    }
}
