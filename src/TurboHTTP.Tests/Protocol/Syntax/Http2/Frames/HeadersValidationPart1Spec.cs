using TurboHTTP.Protocol.Syntax.Http2.Client;
using TurboHTTP.Protocol.Syntax.Http2.Hpack;

namespace TurboHTTP.Tests.Protocol.Syntax.Http2.Frames;

public sealed class Http2ResponseHeaderValidationSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1.2.2")]
    public void ValidateResponseHeaders_should_accept_valid_status_only_response()
    {
        var headers = Decode((":status", "200"));
        Http2ClientDecoder.ValidateResponseHeaders(headers);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1.2.2")]
    public void ValidateResponseHeaders_should_accept_status_with_regular_headers()
    {
        var headers = Decode(
            (":status", "200"),
            ("content-type", "text/plain"),
            ("content-length", "13"));
        Http2ClientDecoder.ValidateResponseHeaders(headers);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1.2.2")]
    public void ValidateResponseHeaders_should_reject_missing_status()
    {
        var headers = Decode(("content-type", "text/plain"));
        var ex = Assert.Throws<HttpProtocolException>(() => Http2ClientDecoder.ValidateResponseHeaders(headers));
        Assert.Contains(":status", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1.2.2")]
    public void ValidateResponseHeaders_should_reject_duplicate_status()
    {
        var headers = Decode((":status", "200"), (":status", "201"));
        var ex = Assert.Throws<HttpProtocolException>(() => Http2ClientDecoder.ValidateResponseHeaders(headers));
        Assert.Contains("Duplicate", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1.2.2")]
    public void ValidateResponseHeaders_should_reject_status_after_regular_header()
    {
        var headers = Decode(("content-type", "text/plain"), (":status", "200"));
        Assert.Throws<HttpProtocolException>(() => Http2ClientDecoder.ValidateResponseHeaders(headers));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1.2.2")]
    public void ValidateResponseHeaders_should_reject_request_pseudo_header_in_response()
    {
        var headers = Decode((":status", "200"), (":method", "GET"));
        var ex = Assert.Throws<HttpProtocolException>(() => Http2ClientDecoder.ValidateResponseHeaders(headers));
        Assert.Contains(":method", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1.2.2")]
    public void ValidateResponseHeaders_should_reject_unknown_pseudo_header()
    {
        var headers = Decode((":status", "200"), (":foobar", "baz"));
        var ex = Assert.Throws<HttpProtocolException>(() => Http2ClientDecoder.ValidateResponseHeaders(headers));
        Assert.Contains(":foobar", ex.Message);
    }

    [Theory(Timeout = 5000)]
    [InlineData("100")]
    [InlineData("200")]
    [InlineData("301")]
    [InlineData("404")]
    [InlineData("500")]
    [InlineData("599")]
    [Trait("RFC", "RFC9113-8.1.2.2")]
    public void ValidateResponseHeaders_should_accept_all_valid_status_codes(string statusCode)
    {
        var headers = Decode((":status", statusCode));
        Http2ClientDecoder.ValidateResponseHeaders(headers);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1.2.2")]
    public void ValidateResponseHeaders_should_include_rfc_section_in_error()
    {
        var headers = Decode(("content-type", "text/plain"));
        var ex = Assert.Throws<HttpProtocolException>(() => Http2ClientDecoder.ValidateResponseHeaders(headers));
        Assert.Contains("RFC 9113", ex.Message);
    }

    private static List<HpackHeader> Decode(params (string Name, string Value)[] headers)
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var block = encoder.Encode(headers);
        return new HpackDecoder().Decode(block.Span);
    }
}