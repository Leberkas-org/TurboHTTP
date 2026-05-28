using TurboHTTP.Protocol.Syntax.Http3;
using TurboHTTP.Protocol.Syntax.Http3.Qpack;
using TurboHTTP.Protocol.Syntax.Http3.Server;

namespace TurboHTTP.Tests.Protocol.Syntax.Http3.Server.Security;

public sealed class Http3ServerSecuritySpec
{
    private readonly QpackTableSync _encoderSync = new(0, 0, 0, 0);
    private readonly QpackTableSync _decoderSync = new(0, 0, 0, 0);

    private HeadersFrame EncodeAndSync(List<(string Name, string Value)> headers)
    {
        var block = _encoderSync.Encoder.Encode(headers);
        var instr = _encoderSync.Encoder.EncoderInstructions;
        if (!instr.IsEmpty)
        {
            _decoderSync.ProcessEncoderInstructions(instr.Span);
        }

        return new HeadersFrame(block);
    }

    private static StreamState MakeState(long id = 1)
    {
        var s = new StreamState();
        s.Initialize(id);
        return s;
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.2.2")]
    public void Field_section_exceeding_max_size_should_be_rejected()
    {
        var decoder = new Http3ServerDecoder(_decoderSync, maxFieldSectionSize: 128);

        var headers = new List<(string Name, string Value)>
        {
            (":method", "GET"),
            (":path", "/"),
            (":scheme", "https"),
            (":authority", "example.com"),
            ("x-large", new string('x', 150)),
        };

        var frame = EncodeAndSync(headers);
        var state = MakeState();

        var ex = Assert.Throws<HttpProtocolException>(() =>
            decoder.DecodeHeadersToFeature(frame, state, endStream: true));
        Assert.Contains("SETTINGS_MAX_FIELD_SECTION_SIZE", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.2.2")]
    public void Many_small_headers_exceeding_total_field_section_size_should_be_rejected()
    {
        var decoder = new Http3ServerDecoder(_decoderSync, maxFieldSectionSize: 256);

        var headers = new List<(string Name, string Value)>
        {
            (":method", "GET"),
            (":path", "/"),
            (":scheme", "https"),
            (":authority", "example.com"),
            ("x-header-1", new string('a', 35)),
            ("x-header-2", new string('b', 35)),
            ("x-header-3", new string('c', 35)),
            ("x-header-4", new string('d', 35)),
            ("x-header-5", new string('e', 35)),
        };

        var frame = EncodeAndSync(headers);
        var state = MakeState();

        var ex = Assert.Throws<HttpProtocolException>(() =>
            decoder.DecodeHeadersToFeature(frame, state, endStream: true));
        Assert.Contains("SETTINGS_MAX_FIELD_SECTION_SIZE", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.2")]
    public void Uppercase_header_name_should_be_rejected()
    {
        var decoder = new Http3ServerDecoder(_decoderSync);

        var headers = new List<(string Name, string Value)>
        {
            (":method", "GET"),
            (":path", "/"),
            (":scheme", "https"),
            (":authority", "example.com"),
            ("X-Upper", "value"),
        };

        var frame = EncodeAndSync(headers);
        var state = MakeState();

        var ex = Assert.Throws<HttpProtocolException>(() =>
            decoder.DecodeHeadersToFeature(frame, state, endStream: true));
        Assert.Contains("uppercase", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-10.3")]
    public void Header_value_with_null_byte_should_be_rejected()
    {
        var decoder = new Http3ServerDecoder(_decoderSync);

        var headers = new List<(string Name, string Value)>
        {
            (":method", "GET"),
            (":path", "/"),
            (":scheme", "https"),
            (":authority", "example.com"),
            ("x-data", "val\0ue"),
        };

        var frame = EncodeAndSync(headers);
        var state = MakeState();

        var ex = Assert.Throws<HttpProtocolException>(() =>
            decoder.DecodeHeadersToFeature(frame, state, endStream: true));
        Assert.Contains("NUL", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-10.3")]
    public void Empty_header_name_should_be_rejected()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":method", "GET"),
            (":path", "/"),
            (":scheme", "https"),
            (":authority", "example.com"),
            ("", "empty-name"),
        };

        // QPACK encoder rejects empty header names at encoding time (RFC 9204 violation).
        // This is encoder-level defense-in-depth per RFC 9114-10.3.
        var ex = Assert.Throws<QpackException>(() => EncodeAndSync(headers));
        Assert.Contains("empty", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}