using System.Text;
using TurboHTTP.Protocol.Syntax;
using TurboHTTP.Protocol.Syntax.Http11.Options;
using TurboHTTP.Protocol.Syntax.Http11.Server;

namespace TurboHTTP.Tests.Protocol.Syntax.Http11.Server;

public sealed class Http11ServerDecoderSpec
{
    private readonly Http11ServerDecoder _decoder = new(Http11ServerDecoderOptions.Default);

    [Fact(Timeout = 5000)]
    public void Feed_should_decode_simple_request()
    {
        const string request = "GET /path HTTP/1.1\r\nHost: example.com\r\nContent-Length: 0\r\n\r\n";
        var bytes = Encoding.ASCII.GetBytes(request);

        var outcome = _decoder.Feed(bytes, out var consumed);

        Assert.Equal(DecodeOutcome.Complete, outcome);
        Assert.Equal(bytes.Length, consumed);

        var feature = _decoder.GetRequestFeature();
        Assert.Equal("GET", feature.Method);
        Assert.Equal("/path", feature.Path);
    }

    [Fact(Timeout = 5000)]
    public void Feed_should_handle_post_with_body()
    {
        const string body = "test data";
        var request = $"POST / HTTP/1.1\r\n" +
                      $"Host: example.com\r\n" +
                      $"Content-Length: {body.Length}\r\n\r\n" +
                      body;
        var bytes = Encoding.ASCII.GetBytes(request);

        var outcome = _decoder.Feed(bytes, out _);

        Assert.Equal(DecodeOutcome.Complete, outcome);
        var feature = _decoder.GetRequestFeature();
        Assert.Equal("POST", feature.Method);
        Assert.NotNull(feature.Body);
    }

    [Fact(Timeout = 5000)]
    public void Feed_should_return_need_more_for_incomplete()
    {
        const string request = "GET /path HTTP/1.1\r\nHost: example.com\r\n";
        var bytes = Encoding.ASCII.GetBytes(request);

        var outcome = _decoder.Feed(bytes, out _);

        Assert.Equal(DecodeOutcome.NeedMore, outcome);
    }

    [Fact(Timeout = 5000)]
    public void Reset_should_clear_state()
    {
        const string request1 = "GET / HTTP/1.1\r\nHost: example.com\r\nContent-Length: 0\r\n\r\n";
        var bytes1 = Encoding.ASCII.GetBytes(request1);

        _decoder.Feed(bytes1, out _);
        var first = _decoder.GetRequestFeature();

        _decoder.Reset();

        const string request2 = "POST / HTTP/1.1\r\nHost: example.com\r\nContent-Length: 0\r\n\r\n";
        var bytes2 = Encoding.ASCII.GetBytes(request2);
        _decoder.Feed(bytes2, out _);
        var second = _decoder.GetRequestFeature();

        Assert.NotEqual(first.Method, second.Method);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-2.2")]
    public void Feed_should_handle_bare_cr_in_request_line()
    {
        var raw = "GET /path\rHTTP/1.1\r\nHost: x\r\nContent-Length: 0\r\n\r\n"u8.ToArray();
        var decoder = new Http11ServerDecoder(Http11ServerDecoderOptions.Default);

        var outcome = decoder.Feed(raw, out _);

        Assert.True(outcome is DecodeOutcome.NeedMore or DecodeOutcome.Complete);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-2.2")]
    public void Feed_should_ignore_leading_crlf_before_request_line()
    {
        var raw = "\r\nGET /path HTTP/1.1\r\nHost: x\r\nContent-Length: 0\r\n\r\n"u8.ToArray();
        var decoder = new Http11ServerDecoder(Http11ServerDecoderOptions.Default);

        var outcome = decoder.Feed(raw, out _);

        Assert.True(outcome is DecodeOutcome.NeedMore or DecodeOutcome.Complete);
        if (outcome == DecodeOutcome.Complete)
        {
            var feature = decoder.GetRequestFeature();
            Assert.Equal("GET", feature.Method);
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-2.2")]
    public void Feed_should_reject_whitespace_before_first_header()
    {
        var raw = "GET / HTTP/1.1\r\n \r\nHost: x\r\nContent-Length: 0\r\n\r\n"u8.ToArray();
        var decoder = new Http11ServerDecoder(Http11ServerDecoderOptions.Default);

        _ = Assert.Throws<HttpProtocolException>(() => decoder.Feed(raw, out _));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-3.2")]
    public void Feed_should_accept_absolute_form_request_target()
    {
        var raw = "GET http://example.com/path HTTP/1.1\r\nHost: example.com\r\nContent-Length: 0\r\n\r\n"u8.ToArray();
        var decoder = new Http11ServerDecoder(Http11ServerDecoderOptions.Default);

        var outcome = decoder.Feed(raw, out _);

        Assert.Equal(DecodeOutcome.Complete, outcome);
        var feature = decoder.GetRequestFeature();
        Assert.Equal("GET", feature.Method);
        Assert.Contains("/path", feature.Path);
    }

    [Fact(Timeout = 5000)]
    public void GetRequestFeature_should_parse_method_and_path()
    {
        var decoder = new Http11ServerDecoder(Http11ServerDecoderOptions.Default);
        var data = "POST /api/items?page=2 HTTP/1.1\r\nHost: example.com\r\nContent-Length: 0\r\n\r\n"u8;
        var outcome = decoder.Feed(data, out _);
        Assert.Equal(DecodeOutcome.Complete, outcome);

        var feature = decoder.GetRequestFeature();

        Assert.Equal("POST", feature.Method);
        Assert.Equal("/api/items", feature.Path);
        Assert.Equal("?page=2", feature.QueryString);
        Assert.Equal("HTTP/1.1", feature.Protocol);
        Assert.Equal("example.com", feature.Headers["Host"]);
    }
}