using TurboHTTP.Protocol.Http3;
using TurboHTTP.Protocol.Http3.Qpack;

namespace TurboHTTP.Tests.Http3.Streams;

public sealed class PseudoHeaderValidationRequestSpec
{
    [Fact(Timeout = 5000)]
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

        RequestEncoder.ValidatePseudoHeaders(headers);
        // No exception means pass
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3.1")]
    public void Request_missing_method_rejected()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":path", "/"),
            (":scheme", "https"),
            (":authority", "example.com"),
        };

        var ex = Assert.Throws<Http3Exception>(() => RequestEncoder.ValidatePseudoHeaders(headers));
        Assert.Equal(ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains(":method", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3.1")]
    public void Request_missing_scheme_rejected()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":method", "GET"),
            (":path", "/"),
            (":authority", "example.com"),
        };

        var ex = Assert.Throws<Http3Exception>(() => RequestEncoder.ValidatePseudoHeaders(headers));
        Assert.Equal(ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains(":scheme", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3.1")]
    public void Request_missing_authority_rejected()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":method", "GET"),
            (":path", "/"),
            (":scheme", "https"),
        };

        var ex = Assert.Throws<Http3Exception>(() => RequestEncoder.ValidatePseudoHeaders(headers));
        Assert.Equal(ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains(":authority", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3.1")]
    public void Request_missing_path_rejected()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":method", "GET"),
            (":scheme", "https"),
            (":authority", "example.com"),
        };

        var ex = Assert.Throws<Http3Exception>(() => RequestEncoder.ValidatePseudoHeaders(headers));
        Assert.Equal(ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains(":path", ex.Message);
    }

    [Fact(Timeout = 5000)]
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

        RequestEncoder.ValidatePseudoHeaders(headers);
    }

    [Fact(Timeout = 5000)]
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

        var ex = Assert.Throws<Http3Exception>(() => RequestEncoder.ValidatePseudoHeaders(headers));
        Assert.Equal(ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("Unknown", ex.Message);
        Assert.Contains(":status", ex.Message);
    }

    [Fact(Timeout = 5000)]
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

        var ex = Assert.Throws<Http3Exception>(() => RequestEncoder.ValidatePseudoHeaders(headers));
        Assert.Equal(ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("Unknown", ex.Message);
    }

    [Fact(Timeout = 5000)]
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

        var ex = Assert.Throws<Http3Exception>(() => RequestEncoder.ValidatePseudoHeaders(headers));
        Assert.Equal(ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains(":custom", ex.Message);
    }

    [Fact(Timeout = 5000)]
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

        var ex = Assert.Throws<Http3Exception>(() => RequestEncoder.ValidatePseudoHeaders(headers));
        Assert.Equal(ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("after regular header", ex.Message);
    }

    [Fact(Timeout = 5000)]
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

        var ex = Assert.Throws<Http3Exception>(() => RequestEncoder.ValidatePseudoHeaders(headers));
        Assert.Equal(ErrorCode.MessageError, ex.ErrorCode);
    }

    [Fact(Timeout = 5000)]
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

        var ex = Assert.Throws<Http3Exception>(() => RequestEncoder.ValidatePseudoHeaders(headers));
        Assert.Equal(ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("Duplicate", ex.Message);
    }

    [Fact(Timeout = 5000)]
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

        var ex = Assert.Throws<Http3Exception>(() => RequestEncoder.ValidatePseudoHeaders(headers));
        Assert.Equal(ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("Duplicate", ex.Message);
    }

    [Fact(Timeout = 5000)]
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

        var ex = Assert.Throws<Http3Exception>(() => RequestEncoder.ValidatePseudoHeaders(headers));
        Assert.Equal(ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("Duplicate", ex.Message);
    }

    [Fact(Timeout = 5000)]
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

        var ex = Assert.Throws<Http3Exception>(() => RequestEncoder.ValidatePseudoHeaders(headers));
        Assert.Equal(ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("Duplicate", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3.1")]
    public void Encoder_generates_valid_pseudo_headers_for_get()
    {
        var encoder = new RequestEncoder(new QpackTableSync());
        var decoder = new QpackDecoder(maxTableCapacity: 0);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/path?q=1");
        var frames = encoder.Encode(request);
        var headersFrame = Assert.IsType<HeadersFrame>(frames[0]);
        var headers = decoder.Decode(headersFrame.HeaderBlock.Span);

        Assert.Contains(headers, h => h is { Name: ":method", Value: "GET" });
        Assert.Contains(headers, h => h is { Name: ":path", Value: "/path?q=1" });
        Assert.Contains(headers, h => h is { Name: ":scheme", Value: "https" });
        Assert.Contains(headers, h => h is { Name: ":authority", Value: "example.com" });
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3.1")]
    public void Encoder_generates_valid_pseudo_headers_for_post()
    {
        var encoder = new RequestEncoder(new QpackTableSync());
        var decoder = new QpackDecoder(maxTableCapacity: 0);

        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.example.com:8443/submit");
        var frames = encoder.Encode(request);
        var headersFrame = Assert.IsType<HeadersFrame>(frames[0]);
        var headers = decoder.Decode(headersFrame.HeaderBlock.Span);

        Assert.Contains(headers, h => h is { Name: ":method", Value: "POST" });
        Assert.Contains(headers, h => h is { Name: ":path", Value: "/submit" });
        Assert.Contains(headers, h => h is { Name: ":scheme", Value: "https" });
        Assert.Contains(headers, h => h is { Name: ":authority", Value: "api.example.com:8443" });
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3.1")]
    public void Encoder_pseudo_headers_before_regular()
    {
        var encoder = new RequestEncoder(new QpackTableSync());
        var decoder = new QpackDecoder(maxTableCapacity: 0);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        request.Headers.TryAddWithoutValidation("accept", "text/html");
        request.Headers.TryAddWithoutValidation("user-agent", "TurboHttp");

        var frames = encoder.Encode(request);
        var headersFrame = Assert.IsType<HeadersFrame>(frames[0]);
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