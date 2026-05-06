using System.Text;
using TurboHTTP.Protocol.Http3;
using TurboHTTP.Protocol.Http3.Qpack;

namespace TurboHTTP.Tests.Http3.Streams;

public sealed class Http3RequestEncoderAdvancedSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void Custom_headers_included()
    {
        var encoder = new RequestEncoder(new QpackTableSync());
        var decoder = new QpackDecoder(maxTableCapacity: 0);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        request.Headers.TryAddWithoutValidation("accept", "application/json");
        request.Headers.TryAddWithoutValidation("x-request-id", "abc-123");

        var frames = encoder.Encode(request);
        var headersFrame = Assert.IsType<HeadersFrame>(frames[0]);
        var headers = decoder.Decode(headersFrame.HeaderBlock.Span);

        Assert.Contains(headers, h => h is { Name: "accept", Value: "application/json" });
        Assert.Contains(headers, h => h is { Name: "x-request-id", Value: "abc-123" });
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void Content_headers_included()
    {
        var encoder = new RequestEncoder(new QpackTableSync());
        var decoder = new QpackDecoder(maxTableCapacity: 0);
        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/api")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };

        var frames = encoder.Encode(request);
        var headersFrame = Assert.IsType<HeadersFrame>(frames[0]);
        var headers = decoder.Decode(headersFrame.HeaderBlock.Span);

        Assert.Contains(headers, h => h.Name == "content-type" && h.Value.Contains("application/json"));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void Header_names_lowercased()
    {
        var encoder = new RequestEncoder(new QpackTableSync());
        var decoder = new QpackDecoder(maxTableCapacity: 0);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        request.Headers.TryAddWithoutValidation("Accept-Language", "en-US");

        var frames = encoder.Encode(request);
        var headersFrame = Assert.IsType<HeadersFrame>(frames[0]);
        var headers = decoder.Decode(headersFrame.HeaderBlock.Span);

        Assert.Contains(headers, h => h is { Name: "accept-language", Value: "en-US" });
        Assert.DoesNotContain(headers, h => h.Name == "Accept-Language");
    }


    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.2")]
    [InlineData("connection")]
    [InlineData("transfer-encoding")]
    [InlineData("upgrade")]
    [InlineData("proxy-connection")]
    [InlineData("keep-alive")]
    public void Forbidden_headers_filtered(string forbiddenHeader)
    {
        var encoder = new RequestEncoder(new QpackTableSync());
        var decoder = new QpackDecoder(maxTableCapacity: 0);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        request.Headers.TryAddWithoutValidation(forbiddenHeader, "some-value");

        var frames = encoder.Encode(request);
        var headersFrame = Assert.IsType<HeadersFrame>(frames[0]);
        var headers = decoder.Decode(headersFrame.HeaderBlock.Span);

        Assert.DoesNotContain(headers, h => h.Name == forbiddenHeader);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.2")]
    public void Non_forbidden_headers_preserved()
    {
        var encoder = new RequestEncoder(new QpackTableSync());
        var decoder = new QpackDecoder(maxTableCapacity: 0);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        request.Headers.TryAddWithoutValidation("connection", "keep-alive");
        request.Headers.TryAddWithoutValidation("accept", "*/*");

        var frames = encoder.Encode(request);
        var headersFrame = Assert.IsType<HeadersFrame>(frames[0]);
        var headers = decoder.Decode(headersFrame.HeaderBlock.Span);

        Assert.DoesNotContain(headers, h => h.Name == "connection");
        Assert.Contains(headers, h => h is { Name: "accept", Value: "*/*" });
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3.1")]
    public void Null_request_throws()
    {
        var encoder = new RequestEncoder(new QpackTableSync());
        Assert.Throws<ArgumentNullException>(() => encoder.Encode(null!));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3.1")]
    public void Null_uri_throws()
    {
        var encoder = new RequestEncoder(new QpackTableSync());
        var request = new HttpRequestMessage(HttpMethod.Get, (Uri?)null);
        Assert.Throws<ArgumentNullException>(() => encoder.Encode(request));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3.1")]
    public void Validate_rejects_duplicate_method()
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
    public void Validate_rejects_missing_pseudo_headers()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":method", "GET"),
            // missing :path, :scheme, :authority
        };

        var ex = Assert.Throws<Http3Exception>(() => RequestEncoder.ValidatePseudoHeaders(headers));
        Assert.Equal(ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("Missing", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3.1")]
    public void Validate_rejects_unknown_pseudo_header()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":method", "GET"),
            (":path", "/"),
            (":scheme", "https"),
            (":authority", "example.com"),
            (":unknown", "value"),
        };

        var ex = Assert.Throws<Http3Exception>(() => RequestEncoder.ValidatePseudoHeaders(headers));
        Assert.Equal(ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("Unknown", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3.1")]
    public void Validate_rejects_pseudo_after_regular()
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
        Assert.Contains("after regular", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void Encoder_is_stateful_across_requests()
    {
        var encoder = new RequestEncoder(new QpackTableSync(encoderMaxCapacity: 4096));

        var request1 = new HttpRequestMessage(HttpMethod.Get, "https://example.com/page1");
        var frames1 = encoder.Encode(request1);

        var request2 = new HttpRequestMessage(HttpMethod.Get, "https://example.com/page2");
        var frames2 = encoder.Encode(request2);

        // Both encode successfully
        Assert.NotEmpty(frames1);
        Assert.NotEmpty(frames2);

        // Second request header block is typically smaller due to QPACK dynamic table reuse
        var block1 = Assert.IsType<HeadersFrame>(frames1[0]).HeaderBlock;
        var block2 = Assert.IsType<HeadersFrame>(frames2[0]).HeaderBlock;
        Assert.True(block2.Length <= block1.Length,
            "Second request should benefit from QPACK state (same or smaller header block)");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void Large_body_encoded()
    {
        var encoder = new RequestEncoder(new QpackTableSync());
        var body = new byte[64 * 1024]; // 64 KB
        new Random(42).NextBytes(body);
        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/upload")
        {
            Content = new ByteArrayContent(body),
        };

        var frames = encoder.Encode(request);

        Assert.Equal(2, frames.Count);
        var dataFrame = Assert.IsType<DataFrame>(frames[1]);
        Assert.Equal(body.Length, dataFrame.Data.Length);
        Assert.Equal(body, dataFrame.Data.ToArray());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3.1")]
    public void Root_path_encoded()
    {
        var encoder = new RequestEncoder(new QpackTableSync());
        var decoder = new QpackDecoder(maxTableCapacity: 0);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com");

        var frames = encoder.Encode(request);
        var headersFrame = Assert.IsType<HeadersFrame>(frames[0]);
        var headers = decoder.Decode(headersFrame.HeaderBlock.Span);

        Assert.Contains(headers, h => h is { Name: ":path", Value: "/" });
    }
}