using TurboHTTP.Protocol.Syntax.Http2.Client;
using TurboHTTP.Protocol.Syntax.Http2.Hpack;

namespace TurboHTTP.Tests.Protocol.Syntax.Http2.Frames;

public sealed class Http2ForbiddenHeaderValidationSpec
{
    [Theory(Timeout = 5000)]
    [InlineData("connection")]
    [InlineData("keep-alive")]
    [InlineData("proxy-connection")]
    [InlineData("transfer-encoding")]
    [InlineData("upgrade")]
    [Trait("RFC", "RFC9113-8.2.2")]
    public void ValidateResponseHeaders_should_reject_forbidden_connection_header(string headerName)
    {
        var headers = Decode((":status", "200"), (headerName, "value"));
        var ex = Assert.Throws<HttpProtocolException>(() => Http2ClientDecoder.ValidateResponseHeaders(headers));
        Assert.Contains("forbidden", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.2.2")]
    public void ValidateResponseHeaders_should_reject_te_header_with_non_trailers_value()
    {
        var headers = Decode((":status", "200"), ("te", "gzip"));
        var ex = Assert.Throws<HttpProtocolException>(() => Http2ClientDecoder.ValidateResponseHeaders(headers));
        Assert.Contains("trailers", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.2.2")]
    public void ValidateResponseHeaders_should_accept_te_header_with_trailers_value()
    {
        var headers = Decode((":status", "200"), ("te", "trailers"));
        Http2ClientDecoder.ValidateResponseHeaders(headers);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.2.2")]
    public void ValidateResponseHeaders_should_accept_regular_headers()
    {
        var headers = Decode(
            (":status", "200"),
            ("content-type", "text/html"),
            ("content-length", "1024"),
            ("server", "MyServer/1.0"));
        Http2ClientDecoder.ValidateResponseHeaders(headers);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.2.2")]
    public void ValidateResponseHeaders_should_accept_custom_headers()
    {
        var headers = Decode(
            (":status", "200"),
            ("x-custom-header", "value"),
            ("x-another-header", "another-value"));
        Http2ClientDecoder.ValidateResponseHeaders(headers);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.3")]
    public void ValidateResponseHeaders_should_accept_set_cookie_headers()
    {
        var headers = Decode(
            (":status", "200"),
            ("set-cookie", "session=abc123"));
        Http2ClientDecoder.ValidateResponseHeaders(headers);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.3")]
    public void ValidateResponseHeaders_should_accept_multiple_set_cookie_headers()
    {
        var headers = Decode(
            (":status", "200"),
            ("set-cookie", "session=abc123"),
            ("set-cookie", "tracking=xyz789"));
        Http2ClientDecoder.ValidateResponseHeaders(headers);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.3")]
    public void ValidateResponseHeaders_should_accept_cache_control_header()
    {
        var headers = Decode(
            (":status", "200"),
            ("cache-control", "max-age=3600"));
        Http2ClientDecoder.ValidateResponseHeaders(headers);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.3")]
    public void ValidateResponseHeaders_should_accept_content_encoding_header()
    {
        var headers = Decode(
            (":status", "200"),
            ("content-encoding", "gzip"));
        Http2ClientDecoder.ValidateResponseHeaders(headers);
    }

    private static List<HpackHeader> Decode(params (string Name, string Value)[] headers)
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var block = encoder.Encode(headers);
        return new HpackDecoder().Decode(block.Span);
    }
}