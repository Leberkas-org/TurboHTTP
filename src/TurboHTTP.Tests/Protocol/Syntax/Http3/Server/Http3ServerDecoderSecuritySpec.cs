using TurboHTTP.Protocol.Syntax.Http3;
using TurboHTTP.Protocol.Syntax.Http3.Qpack;
using TurboHTTP.Protocol.Syntax.Http3.Server;

namespace TurboHTTP.Tests.Protocol.Syntax.Http3.Server;

[Trait("Component", "Http3ServerDecoder")]
public sealed class Http3ServerDecoderSecuritySpec
{
    private readonly QpackTableSync _encoderTableSync = new(encoderMaxCapacity: 0, decoderMaxCapacity: 0);
    private readonly QpackTableSync _decoderTableSync = new(encoderMaxCapacity: 0, decoderMaxCapacity: 0);
    private readonly Http3ServerDecoder _decoder;

    public Http3ServerDecoderSecuritySpec()
    {
        _decoder = new Http3ServerDecoder(_decoderTableSync);
    }

    private HeadersFrame EncodeAndSync(List<(string Name, string Value)> headers)
    {
        var headerBlock = _encoderTableSync.Encoder.Encode(headers);
        var instructions = _encoderTableSync.Encoder.EncoderInstructions;
        if (!instructions.IsEmpty)
        {
            _decoderTableSync.ProcessEncoderInstructions(instructions.Span);
        }

        return new HeadersFrame(headerBlock);
    }

    private static StreamState MakeState(long streamId = 1)
    {
        var state = new StreamState();
        state.Initialize(streamId);
        return state;
    }

    #region Pseudo-Header Validation Tests

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3.1")]
    public void DecodeHeaders_should_reject_duplicate_method_pseudo_header()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":method", "GET"),
            (":method", "POST"),
            (":path", "/"),
            (":scheme", "https"),
            (":authority", "example.com"),
        };

        var frame = EncodeAndSync(headers);
        var state = MakeState();

        var ex = Assert.Throws<HttpProtocolException>(() => _decoder.DecodeHeaders(frame, state));
        Assert.Contains(":method", ex.Message);
        Assert.Contains("Duplicate", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3.1")]
    public void DecodeHeaders_should_reject_duplicate_path_pseudo_header()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":method", "GET"),
            (":path", "/first"),
            (":path", "/second"),
            (":scheme", "https"),
            (":authority", "example.com"),
        };

        var frame = EncodeAndSync(headers);
        var state = MakeState();

        var ex = Assert.Throws<HttpProtocolException>(() => _decoder.DecodeHeaders(frame, state));
        Assert.Contains(":path", ex.Message);
        Assert.Contains("Duplicate", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3.1")]
    public void DecodeHeaders_should_reject_pseudo_header_after_regular_header()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":method", "GET"),
            (":path", "/"),
            (":scheme", "https"),
            ("user-agent", "test"),
            (":authority", "example.com"),
        };

        var frame = EncodeAndSync(headers);
        var state = MakeState();

        var ex = Assert.Throws<HttpProtocolException>(() => _decoder.DecodeHeaders(frame, state));
        Assert.Contains("Pseudo-header", ex.Message);
        Assert.Contains("appears", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3.1")]
    public void DecodeHeaders_should_reject_unknown_pseudo_header()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":method", "GET"),
            (":path", "/"),
            (":scheme", "https"),
            (":authority", "example.com"),
            (":custom", "value"),
        };

        var frame = EncodeAndSync(headers);
        var state = MakeState();

        var ex = Assert.Throws<HttpProtocolException>(() => _decoder.DecodeHeaders(frame, state));
        Assert.Contains(":custom", ex.Message);
    }

    #endregion

    #region Forbidden Connection Headers Tests

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.2")]
    public void DecodeHeaders_should_reject_connection_header()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":method", "GET"),
            (":path", "/"),
            (":scheme", "https"),
            (":authority", "example.com"),
            ("connection", "keep-alive"),
        };

        var frame = EncodeAndSync(headers);
        var state = MakeState();

        var ex = Assert.Throws<HttpProtocolException>(() => _decoder.DecodeHeaders(frame, state));
        Assert.Contains("connection", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("forbidden", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.2")]
    public void DecodeHeaders_should_reject_transfer_encoding_header()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":method", "POST"),
            (":path", "/"),
            (":scheme", "https"),
            (":authority", "example.com"),
            ("transfer-encoding", "chunked"),
        };

        var frame = EncodeAndSync(headers);
        var state = MakeState();

        var ex = Assert.Throws<HttpProtocolException>(() => _decoder.DecodeHeaders(frame, state));
        Assert.Contains("transfer-encoding", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("forbidden", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.2")]
    public void DecodeHeaders_should_reject_te_with_non_trailers_value()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":method", "GET"),
            (":path", "/"),
            (":scheme", "https"),
            (":authority", "example.com"),
            ("te", "gzip"),
        };

        var frame = EncodeAndSync(headers);
        var state = MakeState();

        var ex = Assert.Throws<HttpProtocolException>(() => _decoder.DecodeHeaders(frame, state));
        Assert.Contains("te", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("trailers", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.2")]
    public void DecodeHeaders_should_accept_te_trailers()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":method", "GET"),
            (":path", "/"),
            (":scheme", "https"),
            (":authority", "example.com"),
            ("te", "trailers"),
        };

        var frame = EncodeAndSync(headers);
        var state = MakeState();

        var success = _decoder.DecodeHeaders(frame, state);
        Assert.True(success);
    }

    #endregion

    #region CONNECT Edge Cases Tests

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.4")]
    public void DecodeHeaders_CONNECT_with_path_should_reject()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":method", "CONNECT"),
            (":path", "/"),
            (":authority", "example.com:443"),
        };

        var frame = EncodeAndSync(headers);
        var state = MakeState();

        var ex = Assert.Throws<HttpProtocolException>(() => _decoder.DecodeHeaders(frame, state));
        Assert.Contains(":path", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.4")]
    public void DecodeHeaders_CONNECT_with_scheme_should_reject()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":method", "CONNECT"),
            (":scheme", "https"),
            (":authority", "example.com:443"),
        };

        var frame = EncodeAndSync(headers);
        var state = MakeState();

        var ex = Assert.Throws<HttpProtocolException>(() => _decoder.DecodeHeaders(frame, state));
        Assert.Contains(":scheme", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.4")]
    public void DecodeHeaders_CONNECT_without_authority_should_reject()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":method", "CONNECT"),
        };

        var frame = EncodeAndSync(headers);
        var state = MakeState();

        var ex = Assert.Throws<HttpProtocolException>(() => _decoder.DecodeHeaders(frame, state));
        Assert.Contains(":authority", ex.Message);
    }

    #endregion

    #region Field Section Size Tests

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.2.2")]
    public void DecodeHeaders_should_reject_field_section_exceeding_max_size()
    {
        var decoderWithLimit = new Http3ServerDecoder(_decoderTableSync, maxFieldSectionSize: 128);

        var headers = new List<(string Name, string Value)>
        {
            (":method", "GET"),
            (":path", "/"),
            (":scheme", "https"),
            (":authority", "example.com"),
            ("x-large-header", new string('x', 150)),
        };

        var frame = EncodeAndSync(headers);
        var state = MakeState();

        var ex = Assert.Throws<HttpProtocolException>(() => decoderWithLimit.DecodeHeaders(frame, state));
        Assert.Contains("SETTINGS_MAX_FIELD_SECTION_SIZE", ex.Message);
    }

    #endregion
}
