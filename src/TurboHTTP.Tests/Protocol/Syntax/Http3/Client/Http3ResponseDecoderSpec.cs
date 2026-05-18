using System.Net;
using TurboHTTP.Protocol.Syntax.Http3;
using TurboHTTP.Protocol.Syntax.Http3.Client;
using TurboHTTP.Protocol.Syntax.Http3.Qpack;

namespace TurboHTTP.Tests.Protocol.Syntax.Http3.Client;

public sealed class Http3ResponseDecoderSpec
{
    private readonly QpackTableSync _tableSync = new();
    private readonly Http3ClientDecoder _decoder;

    public Http3ResponseDecoderSpec()
    {
        _decoder = new Http3ClientDecoder(_tableSync);
    }

    private HeadersFrame EncodeHeaders(params (string Name, string Value)[] headers)
    {
        return new HeadersFrame(_tableSync.Encoder.Encode(headers));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void DecodeHeaders_should_parse_status_code()
    {
        var state = new StreamState();
        var frame = EncodeHeaders((":status", "200"));

        var result = _decoder.DecodeHeaders(frame, state);

        Assert.True(result);
        Assert.True(state.HasResponse);
        Assert.Equal(HttpStatusCode.OK, state.GetResponse().StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void DecodeHeaders_should_parse_response_headers()
    {
        var state = new StreamState();
        var frame = EncodeHeaders(
            (":status", "200"),
            ("x-custom", "value"),
            ("server", "test"));

        _decoder.DecodeHeaders(frame, state);

        var response = state.GetResponse();
        Assert.Equal("value", response.Headers.GetValues("x-custom").Single());
        Assert.Equal("test", response.Headers.GetValues("server").Single());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void DecodeHeaders_should_capture_content_headers()
    {
        var state = new StreamState();
        var frame = EncodeHeaders(
            (":status", "200"),
            ("content-type", "text/plain"),
            ("content-length", "42"));

        _decoder.DecodeHeaders(frame, state);

        Assert.True(state.HasContentHeaders);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-6.5")]
    public void DecodeHeaders_should_deliver_trailing_headers()
    {
        var state = new StreamState();
        var first = EncodeHeaders((":status", "200"));
        var trailing = EncodeHeaders(("x-checksum", "abc123"), ("server-timing", "dur=42"));

        _decoder.DecodeHeaders(first, state);
        var result = _decoder.DecodeHeaders(trailing, state);

        Assert.False(result);
        var response = state.GetResponse();
        Assert.Equal("abc123", response.TrailingHeaders.GetValues("x-checksum").Single());
        Assert.Equal("dur=42", response.TrailingHeaders.GetValues("server-timing").Single());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-6.5")]
    public void DecodeHeaders_should_filter_prohibited_trailers()
    {
        var state = new StreamState();
        var first = EncodeHeaders((":status", "200"));
        var trailing = EncodeHeaders(("x-custom", "ok"), ("transfer-encoding", "chunked"));

        _decoder.DecodeHeaders(first, state);
        _decoder.DecodeHeaders(trailing, state);

        var response = state.GetResponse();
        Assert.Equal("ok", response.TrailingHeaders.GetValues("x-custom").Single());
        Assert.False(response.TrailingHeaders.Contains("transfer-encoding"));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void DecodeHeaders_should_reject_pseudo_headers_in_trailers()
    {
        var state = new StreamState();
        var first = EncodeHeaders((":status", "200"));
        var trailing = EncodeHeaders((":status", "200"), ("x-trailer", "value"));

        _decoder.DecodeHeaders(first, state);

        // RFC 9114 §4.3: Pseudo-header fields MUST NOT appear in trailer sections
        var ex = Assert.Throws<HttpProtocolException>(() => _decoder.DecodeHeaders(trailing, state));
        Assert.Contains("pseudo", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}