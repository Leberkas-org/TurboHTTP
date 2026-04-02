using TurboHttp.Protocol.Http3;
using TurboHttp.Protocol.Http3.Qpack;

namespace TurboHttp.Tests.Http3.Streams;

/// <summary>
/// RFC 9114 §4.3.2 — Pseudo-header validation for HTTP/3 responses.
/// Covers :status requirements, unknown pseudo-headers, ordering, duplicates, and decoder integration.
/// </summary>
public sealed class PseudoHeaderValidationResponseSpec
{

    [Fact]
    [Trait("RFC", "RFC9114-4.3.2")]
    public void Response_valid_status_accepted()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":status", "200"),
            ("content-type", "text/html"),
        };

        Http3ResponseDecoder.ValidateResponsePseudoHeaders(headers);
    }

    [Fact]
    [Trait("RFC", "RFC9114-4.3.2")]
    public void Response_missing_status_rejected()
    {
        var qpackEncoder = new QpackEncoder(maxTableCapacity: 0);
        var headers = new List<(string Name, string Value)>
        {
            ("content-type", "text/html"),
        };

        var headerBlock = qpackEncoder.Encode(headers);
        var frames = new List<Http3Frame> { new Http3HeadersFrame(headerBlock) };

        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);
        var ex = Assert.Throws<Http3Exception>(() => decoder.Decode(frames));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains(":status", ex.Message);
    }

    [Fact]
    [Trait("RFC", "RFC9114-4.3.2")]
    public void Response_invalid_status_value_rejected()
    {
        var qpackEncoder = new QpackEncoder(maxTableCapacity: 0);
        var headers = new List<(string Name, string Value)>
        {
            (":status", "abc"),
        };

        var headerBlock = qpackEncoder.Encode(headers);
        var frames = new List<Http3Frame> { new Http3HeadersFrame(headerBlock) };

        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);
        var ex = Assert.Throws<Http3Exception>(() => decoder.Decode(frames));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
    }

    [Theory]
    [Trait("RFC", "RFC9114-4.3.2")]
    [InlineData("100")]
    [InlineData("200")]
    [InlineData("301")]
    [InlineData("404")]
    [InlineData("500")]
    public void Response_valid_status_codes_accepted(string status)
    {
        var qpackEncoder = new QpackEncoder(maxTableCapacity: 0);
        var headers = new List<(string Name, string Value)>
        {
            (":status", status),
        };

        var headerBlock = qpackEncoder.Encode(headers);
        var frames = new List<Http3Frame> { new Http3HeadersFrame(headerBlock) };

        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);
        var response = decoder.Decode(frames);
        Assert.Equal(int.Parse(status), (int)response.StatusCode);
    }


    [Fact]
    [Trait("RFC", "RFC9114-4.3.2")]
    public void Response_unknown_pseudo_header_method_rejected()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":status", "200"),
            (":method", "GET"),
        };

        var ex = Assert.Throws<Http3Exception>(
            () => Http3ResponseDecoder.ValidateResponsePseudoHeaders(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains(":method", ex.Message);
    }

    [Fact]
    [Trait("RFC", "RFC9114-4.3.2")]
    public void Response_unknown_pseudo_header_path_rejected()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":status", "200"),
            (":path", "/"),
        };

        var ex = Assert.Throws<Http3Exception>(
            () => Http3ResponseDecoder.ValidateResponsePseudoHeaders(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains(":path", ex.Message);
    }

    [Fact]
    [Trait("RFC", "RFC9114-4.3.2")]
    public void Response_unknown_pseudo_header_custom_rejected()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":status", "200"),
            (":custom", "value"),
        };

        var ex = Assert.Throws<Http3Exception>(
            () => Http3ResponseDecoder.ValidateResponsePseudoHeaders(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains(":custom", ex.Message);
    }


    [Fact]
    [Trait("RFC", "RFC9114-4.3.2")]
    public void Response_pseudo_after_regular_rejected()
    {
        var headers = new List<(string Name, string Value)>
        {
            ("content-type", "text/html"),
            (":status", "200"),
        };

        var ex = Assert.Throws<Http3Exception>(
            () => Http3ResponseDecoder.ValidateResponsePseudoHeaders(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("after regular header", ex.Message);
    }


    [Fact]
    [Trait("RFC", "RFC9114-4.3.2")]
    public void Response_duplicate_status_rejected()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":status", "200"),
            (":status", "301"),
        };

        var ex = Assert.Throws<Http3Exception>(
            () => Http3ResponseDecoder.ValidateResponsePseudoHeaders(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("Duplicate", ex.Message);
    }


    [Fact]
    [Trait("RFC", "RFC9114-4.3.2")]
    public void Decoder_validates_pseudo_headers_in_decode()
    {
        var qpackEncoder = new QpackEncoder(maxTableCapacity: 0);
        // Create a header block with unknown pseudo-header
        var headers = new List<(string Name, string Value)>
        {
            (":status", "200"),
            (":method", "GET"),
        };

        var headerBlock = qpackEncoder.Encode(headers);
        var frames = new List<Http3Frame> { new Http3HeadersFrame(headerBlock) };

        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);
        var ex = Assert.Throws<Http3Exception>(() => decoder.Decode(frames));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
    }
}
