using TurboHttp.Protocol.Http3;
using TurboHttp.Protocol.Http3.Qpack;

namespace TurboHttp.Tests.Http3.Streams;

/// <summary>
/// RFC 9114 §4.3.1 — Pseudo-header validation for HTTP/3 requests.
/// Covers required pseudo-headers, unknown rejection, ordering, duplicates, and encoder output.
/// </summary>
public sealed class PseudoHeaderValidationRequestSpec
{

    [Fact]
    [Trait("RFC", "RFC9114-4.3.1")]
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

    [Fact]
    [Trait("RFC", "RFC9114-4.3.1")]
    public void Request_missing_method_rejected()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":path", "/"),
            (":scheme", "https"),
            (":authority", "example.com"),
        };

        var ex = Assert.Throws<Http3Exception>(
            () => Http3RequestEncoder.ValidatePseudoHeaders(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains(":method", ex.Message);
    }

    [Fact]
    [Trait("RFC", "RFC9114-4.3.1")]
    public void Request_missing_scheme_rejected()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":method", "GET"),
            (":path", "/"),
            (":authority", "example.com"),
        };

        var ex = Assert.Throws<Http3Exception>(
            () => Http3RequestEncoder.ValidatePseudoHeaders(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains(":scheme", ex.Message);
    }

    [Fact]
    [Trait("RFC", "RFC9114-4.3.1")]
    public void Request_missing_authority_rejected()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":method", "GET"),
            (":path", "/"),
            (":scheme", "https"),
        };

        var ex = Assert.Throws<Http3Exception>(
            () => Http3RequestEncoder.ValidatePseudoHeaders(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains(":authority", ex.Message);
    }

    [Fact]
    [Trait("RFC", "RFC9114-4.3.1")]
    public void Request_missing_path_rejected()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":method", "GET"),
            (":scheme", "https"),
            (":authority", "example.com"),
        };

        var ex = Assert.Throws<Http3Exception>(
            () => Http3RequestEncoder.ValidatePseudoHeaders(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains(":path", ex.Message);
    }

    [Fact]
    [Trait("RFC", "RFC9114-4.3.1")]
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


    [Fact]
    [Trait("RFC", "RFC9114-4.3.1")]
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

        var ex = Assert.Throws<Http3Exception>(
            () => Http3RequestEncoder.ValidatePseudoHeaders(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("Unknown", ex.Message);
        Assert.Contains(":status", ex.Message);
    }

    [Fact]
    [Trait("RFC", "RFC9114-4.3.1")]
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

        var ex = Assert.Throws<Http3Exception>(
            () => Http3RequestEncoder.ValidatePseudoHeaders(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("Unknown", ex.Message);
    }

    [Fact]
    [Trait("RFC", "RFC9114-4.3.1")]
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

        var ex = Assert.Throws<Http3Exception>(
            () => Http3RequestEncoder.ValidatePseudoHeaders(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains(":custom", ex.Message);
    }


    [Fact]
    [Trait("RFC", "RFC9114-4.3.1")]
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

        var ex = Assert.Throws<Http3Exception>(
            () => Http3RequestEncoder.ValidatePseudoHeaders(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("after regular header", ex.Message);
    }

    [Fact]
    [Trait("RFC", "RFC9114-4.3.1")]
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

        var ex = Assert.Throws<Http3Exception>(
            () => Http3RequestEncoder.ValidatePseudoHeaders(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
    }


    [Fact]
    [Trait("RFC", "RFC9114-4.3.1")]
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

        var ex = Assert.Throws<Http3Exception>(
            () => Http3RequestEncoder.ValidatePseudoHeaders(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("Duplicate", ex.Message);
    }

    [Fact]
    [Trait("RFC", "RFC9114-4.3.1")]
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

        var ex = Assert.Throws<Http3Exception>(
            () => Http3RequestEncoder.ValidatePseudoHeaders(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("Duplicate", ex.Message);
    }

    [Fact]
    [Trait("RFC", "RFC9114-4.3.1")]
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

        var ex = Assert.Throws<Http3Exception>(
            () => Http3RequestEncoder.ValidatePseudoHeaders(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("Duplicate", ex.Message);
    }

    [Fact]
    [Trait("RFC", "RFC9114-4.3.1")]
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

        var ex = Assert.Throws<Http3Exception>(
            () => Http3RequestEncoder.ValidatePseudoHeaders(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("Duplicate", ex.Message);
    }


    [Fact]
    [Trait("RFC", "RFC9114-4.3.1")]
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

    [Fact]
    [Trait("RFC", "RFC9114-4.3.1")]
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

    [Fact]
    [Trait("RFC", "RFC9114-4.3.1")]
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
}
