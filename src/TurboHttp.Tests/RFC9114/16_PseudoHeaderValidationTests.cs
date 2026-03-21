using System.Collections.Generic;
using System.Net.Http;
using TurboHttp.Protocol.RFC9114;
using TurboHttp.Protocol.RFC9204;
using Xunit;

namespace TurboHttp.Tests.RFC9114;

/// <summary>
/// RFC 9114 §4.3 — Pseudo-header validation for HTTP/3 requests and responses.
/// Covers required pseudo-headers, unknown rejection, ordering, and duplicates.
/// </summary>
public sealed class PseudoHeaderValidationTests
{
    // ───────────────────────── Request: Required Pseudo-Headers ─────────────────────────

    [Fact(DisplayName = "RFC-9114-4.3.1-ph-001: Valid request has all four required pseudo-headers")]
    public void Request_valid_with_all_pseudo_headers()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":method", "GET"),
            (":path", "/"),
            (":scheme", "https"),
            (":authority", "example.com"),
        };

        Http3RequestEncoder.ValidatePseudoHeaders(headers);
        // No exception means pass
    }

    [Fact(DisplayName = "RFC-9114-4.3.1-ph-002: Missing :method rejected")]
    public void Request_missing_method_rejected()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":path", "/"),
            (":scheme", "https"),
            (":authority", "example.com"),
        };

        var ex = Assert.Throws<Http3ConnectionException>(
            () => Http3RequestEncoder.ValidatePseudoHeaders(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains(":method", ex.Message);
    }

    [Fact(DisplayName = "RFC-9114-4.3.1-ph-003: Missing :scheme rejected")]
    public void Request_missing_scheme_rejected()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":method", "GET"),
            (":path", "/"),
            (":authority", "example.com"),
        };

        var ex = Assert.Throws<Http3ConnectionException>(
            () => Http3RequestEncoder.ValidatePseudoHeaders(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains(":scheme", ex.Message);
    }

    [Fact(DisplayName = "RFC-9114-4.3.1-ph-004: Missing :authority rejected")]
    public void Request_missing_authority_rejected()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":method", "GET"),
            (":path", "/"),
            (":scheme", "https"),
        };

        var ex = Assert.Throws<Http3ConnectionException>(
            () => Http3RequestEncoder.ValidatePseudoHeaders(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains(":authority", ex.Message);
    }

    [Fact(DisplayName = "RFC-9114-4.3.1-ph-005: Missing :path rejected")]
    public void Request_missing_path_rejected()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":method", "GET"),
            (":scheme", "https"),
            (":authority", "example.com"),
        };

        var ex = Assert.Throws<Http3ConnectionException>(
            () => Http3RequestEncoder.ValidatePseudoHeaders(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains(":path", ex.Message);
    }

    [Fact(DisplayName = "RFC-9114-4.3.1-ph-006: Valid request with regular headers after pseudo-headers")]
    public void Request_valid_with_regular_headers_after()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":method", "POST"),
            (":path", "/api/data"),
            (":scheme", "https"),
            (":authority", "api.example.com"),
            ("content-type", "application/json"),
            ("accept", "application/json"),
        };

        Http3RequestEncoder.ValidatePseudoHeaders(headers);
    }

    // ───────────────────────── Request: Unknown Pseudo-Headers ─────────────────────────

    [Fact(DisplayName = "RFC-9114-4.3.1-ph-007: Unknown pseudo-header :status in request rejected")]
    public void Request_unknown_pseudo_header_status_rejected()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":method", "GET"),
            (":path", "/"),
            (":scheme", "https"),
            (":authority", "example.com"),
            (":status", "200"),
        };

        var ex = Assert.Throws<Http3ConnectionException>(
            () => Http3RequestEncoder.ValidatePseudoHeaders(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("Unknown", ex.Message);
        Assert.Contains(":status", ex.Message);
    }

    [Fact(DisplayName = "RFC-9114-4.3.1-ph-008: Unknown pseudo-header :protocol rejected")]
    public void Request_unknown_pseudo_header_protocol_rejected()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":method", "GET"),
            (":path", "/"),
            (":scheme", "https"),
            (":authority", "example.com"),
            (":protocol", "websocket"),
        };

        var ex = Assert.Throws<Http3ConnectionException>(
            () => Http3RequestEncoder.ValidatePseudoHeaders(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("Unknown", ex.Message);
    }

    [Fact(DisplayName = "RFC-9114-4.3.1-ph-009: Unknown pseudo-header :custom rejected")]
    public void Request_unknown_pseudo_header_custom_rejected()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":method", "GET"),
            (":path", "/"),
            (":scheme", "https"),
            (":authority", "example.com"),
            (":custom", "value"),
        };

        var ex = Assert.Throws<Http3ConnectionException>(
            () => Http3RequestEncoder.ValidatePseudoHeaders(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains(":custom", ex.Message);
    }

    // ───────────────────────── Request: Ordering ─────────────────────────

    [Fact(DisplayName = "RFC-9114-4.3.1-ph-010: Pseudo-header after regular header rejected")]
    public void Request_pseudo_after_regular_rejected()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":method", "GET"),
            (":path", "/"),
            ("accept", "text/html"),
            (":scheme", "https"),
            (":authority", "example.com"),
        };

        var ex = Assert.Throws<Http3ConnectionException>(
            () => Http3RequestEncoder.ValidatePseudoHeaders(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("after regular header", ex.Message);
    }

    [Fact(DisplayName = "RFC-9114-4.3.1-ph-011: All pseudo-headers after regular header rejected")]
    public void Request_all_pseudo_after_regular_rejected()
    {
        var headers = new List<(string Name, string Value)>
        {
            ("host", "example.com"),
            (":method", "GET"),
            (":path", "/"),
            (":scheme", "https"),
            (":authority", "example.com"),
        };

        var ex = Assert.Throws<Http3ConnectionException>(
            () => Http3RequestEncoder.ValidatePseudoHeaders(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
    }

    // ───────────────────────── Request: Duplicates ─────────────────────────

    [Fact(DisplayName = "RFC-9114-4.3.1-ph-012: Duplicate :method rejected")]
    public void Request_duplicate_method_rejected()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":method", "GET"),
            (":method", "POST"),
            (":path", "/"),
            (":scheme", "https"),
            (":authority", "example.com"),
        };

        var ex = Assert.Throws<Http3ConnectionException>(
            () => Http3RequestEncoder.ValidatePseudoHeaders(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("Duplicate", ex.Message);
    }

    [Fact(DisplayName = "RFC-9114-4.3.1-ph-013: Duplicate :path rejected")]
    public void Request_duplicate_path_rejected()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":method", "GET"),
            (":path", "/"),
            (":path", "/other"),
            (":scheme", "https"),
            (":authority", "example.com"),
        };

        var ex = Assert.Throws<Http3ConnectionException>(
            () => Http3RequestEncoder.ValidatePseudoHeaders(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("Duplicate", ex.Message);
    }

    [Fact(DisplayName = "RFC-9114-4.3.1-ph-014: Duplicate :scheme rejected")]
    public void Request_duplicate_scheme_rejected()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":method", "GET"),
            (":path", "/"),
            (":scheme", "https"),
            (":scheme", "http"),
            (":authority", "example.com"),
        };

        var ex = Assert.Throws<Http3ConnectionException>(
            () => Http3RequestEncoder.ValidatePseudoHeaders(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("Duplicate", ex.Message);
    }

    [Fact(DisplayName = "RFC-9114-4.3.1-ph-015: Duplicate :authority rejected")]
    public void Request_duplicate_authority_rejected()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":method", "GET"),
            (":path", "/"),
            (":scheme", "https"),
            (":authority", "example.com"),
            (":authority", "other.com"),
        };

        var ex = Assert.Throws<Http3ConnectionException>(
            () => Http3RequestEncoder.ValidatePseudoHeaders(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("Duplicate", ex.Message);
    }

    // ───────────────────────── Request: Integration via Encoder ─────────────────────────

    [Fact(DisplayName = "RFC-9114-4.3.1-ph-016: Encoder generates valid pseudo-headers for GET")]
    public void Encoder_generates_valid_pseudo_headers_for_get()
    {
        var encoder = new Http3RequestEncoder(maxTableCapacity: 0);
        var decoder = new QpackDecoder(maxTableCapacity: 0);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/path?q=1");
        var frames = encoder.Encode(request);
        var headersFrame = Assert.IsType<Http3HeadersFrame>(frames[0]);
        var headers = decoder.Decode(headersFrame.HeaderBlock.Span);

        Assert.Contains(headers, h => h.Name == ":method" && h.Value == "GET");
        Assert.Contains(headers, h => h.Name == ":path" && h.Value == "/path?q=1");
        Assert.Contains(headers, h => h.Name == ":scheme" && h.Value == "https");
        Assert.Contains(headers, h => h.Name == ":authority" && h.Value == "example.com");
    }

    [Fact(DisplayName = "RFC-9114-4.3.1-ph-017: Encoder generates valid pseudo-headers for POST")]
    public void Encoder_generates_valid_pseudo_headers_for_post()
    {
        var encoder = new Http3RequestEncoder(maxTableCapacity: 0);
        var decoder = new QpackDecoder(maxTableCapacity: 0);

        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.example.com:8443/submit");
        var frames = encoder.Encode(request);
        var headersFrame = Assert.IsType<Http3HeadersFrame>(frames[0]);
        var headers = decoder.Decode(headersFrame.HeaderBlock.Span);

        Assert.Contains(headers, h => h.Name == ":method" && h.Value == "POST");
        Assert.Contains(headers, h => h.Name == ":path" && h.Value == "/submit");
        Assert.Contains(headers, h => h.Name == ":scheme" && h.Value == "https");
        Assert.Contains(headers, h => h.Name == ":authority" && h.Value == "api.example.com:8443");
    }

    [Fact(DisplayName = "RFC-9114-4.3.1-ph-018: Encoder places pseudo-headers before regular headers")]
    public void Encoder_pseudo_headers_before_regular()
    {
        var encoder = new Http3RequestEncoder(maxTableCapacity: 0);
        var decoder = new QpackDecoder(maxTableCapacity: 0);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        request.Headers.TryAddWithoutValidation("accept", "text/html");
        request.Headers.TryAddWithoutValidation("user-agent", "TurboHttp");

        var frames = encoder.Encode(request);
        var headersFrame = Assert.IsType<Http3HeadersFrame>(frames[0]);
        var headers = decoder.Decode(headersFrame.HeaderBlock.Span);

        var lastPseudoIndex = -1;
        var firstRegularIndex = int.MaxValue;

        for (var i = 0; i < headers.Count; i++)
        {
            if (headers[i].Name.StartsWith(':'))
            {
                lastPseudoIndex = i;
            }
            else if (firstRegularIndex == int.MaxValue)
            {
                firstRegularIndex = i;
            }
        }

        Assert.True(lastPseudoIndex < firstRegularIndex,
            $"Last pseudo-header at index {lastPseudoIndex} should be before first regular header at index {firstRegularIndex}");
    }

    // ───────────────────────── Response: :status Validation ─────────────────────────

    [Fact(DisplayName = "RFC-9114-4.3.2-ph-019: Valid :status pseudo-header accepted")]
    public void Response_valid_status_accepted()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":status", "200"),
            ("content-type", "text/html"),
        };

        Http3ResponseDecoder.ValidateResponsePseudoHeaders(headers);
    }

    [Fact(DisplayName = "RFC-9114-4.3.2-ph-020: Missing :status detected by decoder")]
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
        var ex = Assert.Throws<Http3ConnectionException>(() => decoder.Decode(frames));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains(":status", ex.Message);
    }

    [Fact(DisplayName = "RFC-9114-4.3.2-ph-021: Invalid :status value rejected")]
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
        var ex = Assert.Throws<Http3ConnectionException>(() => decoder.Decode(frames));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
    }

    [Theory(DisplayName = "RFC-9114-4.3.2-ph-022: Valid status codes accepted")]
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

    // ───────────────────────── Response: Unknown Pseudo-Headers ─────────────────────────

    [Fact(DisplayName = "RFC-9114-4.3.2-ph-023: Unknown pseudo-header :method in response rejected")]
    public void Response_unknown_pseudo_header_method_rejected()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":status", "200"),
            (":method", "GET"),
        };

        var ex = Assert.Throws<Http3ConnectionException>(
            () => Http3ResponseDecoder.ValidateResponsePseudoHeaders(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains(":method", ex.Message);
    }

    [Fact(DisplayName = "RFC-9114-4.3.2-ph-024: Unknown pseudo-header :path in response rejected")]
    public void Response_unknown_pseudo_header_path_rejected()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":status", "200"),
            (":path", "/"),
        };

        var ex = Assert.Throws<Http3ConnectionException>(
            () => Http3ResponseDecoder.ValidateResponsePseudoHeaders(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains(":path", ex.Message);
    }

    [Fact(DisplayName = "RFC-9114-4.3.2-ph-025: Unknown pseudo-header :custom in response rejected")]
    public void Response_unknown_pseudo_header_custom_rejected()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":status", "200"),
            (":custom", "value"),
        };

        var ex = Assert.Throws<Http3ConnectionException>(
            () => Http3ResponseDecoder.ValidateResponsePseudoHeaders(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains(":custom", ex.Message);
    }

    // ───────────────────────── Response: Ordering ─────────────────────────

    [Fact(DisplayName = "RFC-9114-4.3.2-ph-026: :status after regular header rejected")]
    public void Response_pseudo_after_regular_rejected()
    {
        var headers = new List<(string Name, string Value)>
        {
            ("content-type", "text/html"),
            (":status", "200"),
        };

        var ex = Assert.Throws<Http3ConnectionException>(
            () => Http3ResponseDecoder.ValidateResponsePseudoHeaders(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("after regular header", ex.Message);
    }

    // ───────────────────────── Response: Duplicates ─────────────────────────

    [Fact(DisplayName = "RFC-9114-4.3.2-ph-027: Duplicate :status rejected")]
    public void Response_duplicate_status_rejected()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":status", "200"),
            (":status", "301"),
        };

        var ex = Assert.Throws<Http3ConnectionException>(
            () => Http3ResponseDecoder.ValidateResponsePseudoHeaders(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("Duplicate", ex.Message);
    }

    // ───────────────────────── Response: Integration via Decoder ─────────────────────────

    [Fact(DisplayName = "RFC-9114-4.3.2-ph-028: Decoder validates pseudo-headers in full decode path")]
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
        var ex = Assert.Throws<Http3ConnectionException>(() => decoder.Decode(frames));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
    }
}
