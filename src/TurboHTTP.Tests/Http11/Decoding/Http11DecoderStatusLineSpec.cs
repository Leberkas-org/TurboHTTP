using System.Net;
using System.Text;
using TurboHTTP.Protocol;
using Decoder = TurboHTTP.Protocol.Http11.Decoder;

namespace TurboHTTP.Tests.Http11.Decoding;

public sealed class Http11DecoderStatusLineSpec
{
    private readonly Decoder _decoder = new();

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    public async Task Http11Decoder_should_decode_when_simple_ok_with_content_length()
    {
        const string body = "Hello, World!";
        var raw = BuildResponse(200, "OK", body, ("Content-Length", body.Length.ToString()));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Single(responses);
        Assert.Equal((int)HttpStatusCode.OK, (int)responses[0].StatusCode);
        var result = await responses[0].Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Hello, World!", result);
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    [InlineData(200, "OK", HttpStatusCode.OK)]
    [InlineData(201, "Created", HttpStatusCode.Created)]
    [InlineData(301, "Moved Permanently", HttpStatusCode.MovedPermanently)]
    [InlineData(400, "Bad Request", HttpStatusCode.BadRequest)]
    [InlineData(404, "Not Found", HttpStatusCode.NotFound)]
    [InlineData(500, "Internal Server Error", HttpStatusCode.InternalServerError)]
    public void Http11Decoder_should_parse_correctly_when_known_status_code(int code, string reason,
        HttpStatusCode expected)
    {
        var raw = BuildResponse(code, reason, "", ("Content-Length", "0"));
        _decoder.TryDecode(raw, out var responses);

        Assert.Equal(expected, responses[0].StatusCode);
        Assert.Equal(reason, responses[0].ReasonPhrase);
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    [InlineData(200, "OK", HttpStatusCode.OK)]
    [InlineData(201, "Created", HttpStatusCode.Created)]
    [InlineData(202, "Accepted", HttpStatusCode.Accepted)]
    [InlineData(203, "Non-Authoritative Information", HttpStatusCode.NonAuthoritativeInformation)]
    [InlineData(204, "No Content", HttpStatusCode.NoContent)]
    [InlineData(205, "Reset Content", HttpStatusCode.ResetContent)]
    [InlineData(206, "Partial Content", HttpStatusCode.PartialContent)]
    [InlineData(207, "Multi-Status", (HttpStatusCode)207)]
    public void Http11Decoder_should_parse_correctly_when_2xx_status_code(int code, string reason,
        HttpStatusCode expected)
    {
        var raw = BuildResponse(code, reason, "", ("Content-Length", "0"));
        _decoder.TryDecode(raw, out var responses);

        Assert.Equal(expected, responses[0].StatusCode);
        Assert.Equal(reason, responses[0].ReasonPhrase);
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    [InlineData(300, "Multiple Choices", HttpStatusCode.MultipleChoices)]
    [InlineData(301, "Moved Permanently", HttpStatusCode.MovedPermanently)]
    [InlineData(302, "Found", HttpStatusCode.Found)]
    [InlineData(303, "See Other", HttpStatusCode.SeeOther)]
    [InlineData(304, "Not Modified", HttpStatusCode.NotModified)]
    [InlineData(307, "Temporary Redirect", HttpStatusCode.TemporaryRedirect)]
    [InlineData(308, "Permanent Redirect", HttpStatusCode.PermanentRedirect)]
    public void Http11Decoder_should_parse_correctly_when_3xx_status_code(int code, string reason,
        HttpStatusCode expected)
    {
        var raw = BuildResponse(code, reason, "", ("Content-Length", "0"));
        _decoder.TryDecode(raw, out var responses);

        Assert.Equal(expected, responses[0].StatusCode);
        Assert.Equal(reason, responses[0].ReasonPhrase);
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    [InlineData(400, "Bad Request", HttpStatusCode.BadRequest)]
    [InlineData(401, "Unauthorized", HttpStatusCode.Unauthorized)]
    [InlineData(403, "Forbidden", HttpStatusCode.Forbidden)]
    [InlineData(404, "Not Found", HttpStatusCode.NotFound)]
    [InlineData(405, "Method Not Allowed", HttpStatusCode.MethodNotAllowed)]
    [InlineData(408, "Request Timeout", HttpStatusCode.RequestTimeout)]
    [InlineData(409, "Conflict", HttpStatusCode.Conflict)]
    [InlineData(410, "Gone", HttpStatusCode.Gone)]
    [InlineData(413, "Payload Too Large", HttpStatusCode.RequestEntityTooLarge)]
    [InlineData(415, "Unsupported Media Type", HttpStatusCode.UnsupportedMediaType)]
    [InlineData(422, "Unprocessable Entity", (HttpStatusCode)422)]
    [InlineData(429, "Too Many Requests", (HttpStatusCode)429)]
    public void Http11Decoder_should_parse_correctly_when_4xx_status_code(int code, string reason,
        HttpStatusCode expected)
    {
        var raw = BuildResponse(code, reason, "", ("Content-Length", "0"));
        _decoder.TryDecode(raw, out var responses);

        Assert.Equal(expected, responses[0].StatusCode);
        Assert.Equal(reason, responses[0].ReasonPhrase);
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    [InlineData(500, "Internal Server Error", HttpStatusCode.InternalServerError)]
    [InlineData(501, "Not Implemented", HttpStatusCode.NotImplemented)]
    [InlineData(502, "Bad Gateway", HttpStatusCode.BadGateway)]
    [InlineData(503, "Service Unavailable", HttpStatusCode.ServiceUnavailable)]
    [InlineData(504, "Gateway Timeout", HttpStatusCode.GatewayTimeout)]
    public void Http11Decoder_should_parse_correctly_when_5xx_status_code(int code, string reason,
        HttpStatusCode expected)
    {
        var raw = BuildResponse(code, reason, "", ("Content-Length", "0"));
        _decoder.TryDecode(raw, out var responses);

        Assert.Equal(expected, responses[0].StatusCode);
        Assert.Equal(reason, responses[0].ReasonPhrase);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    public void Http11Decoder_should_have_no_body_when_1xx_informational()
    {
        var raw = "HTTP/1.1 103 Early Hints\r\nLink: </style.css>; rel=preload\r\n\r\n"u8.ToArray();
        var raw200 = BuildResponse(200, "OK", "body", ("Content-Length", "4"));

        var combined = new byte[raw.Length + raw200.Length];
        raw.CopyTo(combined, 0);
        raw200.Span.CopyTo(combined.AsSpan(raw.Length));

        var decoded = _decoder.TryDecode(combined, out var responses);

        Assert.True(decoded);
        Assert.Single(responses); // 1xx is skipped
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    [InlineData(100, "Continue")]
    [InlineData(101, "Switching Protocols")]
    [InlineData(102, "Processing")]
    [InlineData(103, "Early Hints")]
    public void Http11Decoder_should_parse_with_no_body_when_1xx_code(int code, string reason)
    {
        var raw1Xx = Encoding.ASCII.GetBytes($"HTTP/1.1 {code} {reason}\r\n\r\n");
        var raw200 = BuildResponse(200, "OK", "data", ("Content-Length", "4"));

        var combined = new byte[raw1Xx.Length + raw200.Length];
        raw1Xx.CopyTo(combined, 0);
        raw200.Span.CopyTo(combined.AsSpan(raw1Xx.Length));

        var decoded = _decoder.TryDecode(combined, out var responses);

        Assert.True(decoded);
        Assert.Single(responses); // 1xx skipped
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    public void Http11Decoder_should_decode_correctly_when_100_continue_before_200()
    {
        var raw100 = "HTTP/1.1 100 Continue\r\n\r\n"u8.ToArray();
        var raw200 = BuildResponse(200, "OK", "body", ("Content-Length", "4"));

        var combined = new byte[raw100.Length + raw200.Length];
        raw100.CopyTo(combined, 0);
        raw200.Span.CopyTo(combined.AsSpan(raw100.Length));

        var decoded = _decoder.TryDecode(combined, out var responses);

        Assert.True(decoded);
        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    public async Task Http11Decoder_should_process_all_when_multiple_1xx_then_200()
    {
        var raw100 = "HTTP/1.1 100 Continue\r\n\r\n"u8.ToArray();
        var raw102 = "HTTP/1.1 102 Processing\r\n\r\n"u8.ToArray();
        var raw103 = "HTTP/1.1 103 Early Hints\r\nLink: </style.css>\r\n\r\n"u8.ToArray();
        var raw200 = BuildResponse(200, "OK", "final", ("Content-Length", "5"));

        var combined = new byte[raw100.Length + raw102.Length + raw103.Length + raw200.Length];
        raw100.CopyTo(combined, 0);
        raw102.CopyTo(combined, raw100.Length);
        raw103.CopyTo(combined, raw100.Length + raw102.Length);
        raw200.Span.CopyTo(combined.AsSpan(raw100.Length + raw102.Length + raw103.Length));

        var decoded = _decoder.TryDecode(combined, out var responses);

        Assert.True(decoded);
        Assert.Single(responses); // All 1xx skipped
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        var body = await responses[0].Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("final", body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    public void Http11Decoder_should_parse_when_custom_status_599()
    {
        var raw = "HTTP/1.1 599 Custom\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Equal(599, (int)responses[0].StatusCode);
        Assert.Equal("Custom", responses[0].ReasonPhrase);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    public void Http11Decoder_should_error_when_status_greater_than_599()
    {
        var raw = "HTTP/1.1 600 Invalid\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var ex = Assert.Throws<HttpDecoderException>(() => _decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecoderError.InvalidStatusLine, ex.DecodeError);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    public void Http11Decoder_should_be_valid_when_empty_reason_phrase()
    {
        var raw = "HTTP/1.1 200 \r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        Assert.True(string.IsNullOrWhiteSpace(responses[0].ReasonPhrase));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    public void Http11Decoder_should_have_no_body_when_response_204_no_content()
    {
        var raw = BuildResponse(204, "No Content", "", ("Content-Length", "0"));
        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.NoContent, responses[0].StatusCode);
        Assert.Equal(0, responses[0].Content.Headers.ContentLength);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    public void Http11Decoder_should_skip_when_100_continue()
    {
        var raw100 = "HTTP/1.1 100 Continue\r\n\r\n"u8.ToArray();
        var raw200 = BuildResponse(200, "OK", "body", ("Content-Length", "4"));

        var combined = new byte[raw100.Length + raw200.Length];
        raw100.CopyTo(combined);
        raw200.Span.CopyTo(combined.AsSpan(raw100.Length));

        var decoded = _decoder.TryDecode(combined, out var responses);

        Assert.True(decoded);
        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    public void Http11Decoder_should_parse_correctly_when_response_304_no_body()
    {
        var raw = "HTTP/1.1 304 Not Modified\r\nETag: \"abc\"\r\n\r\n"u8.ToArray();
        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Equal(HttpStatusCode.NotModified, responses[0].StatusCode);
        Assert.Equal(0, responses[0].Content.Headers.ContentLength);
    }

    private static ReadOnlyMemory<byte> BuildResponse(int code, string reason, string body,
        params (string Name, string Value)[] headers)
    {
        var sb = new StringBuilder();
        sb.Append($"HTTP/1.1 {code} {reason}\r\n");
        foreach (var (name, value) in headers)
        {
            sb.Append($"{name}: {value}\r\n");
        }

        sb.Append("\r\n");
        sb.Append(body);
        return Encoding.UTF8.GetBytes(sb.ToString());
    }
}