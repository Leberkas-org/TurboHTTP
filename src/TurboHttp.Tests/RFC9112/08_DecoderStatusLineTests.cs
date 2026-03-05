using System.Net;
using System.Text;
using TurboHttp.Protocol;

namespace TurboHttp.Tests.RFC9112;

public sealed class Http11DecoderStatusLineTests
{
    private readonly Http11Decoder _decoder = new();

    [Fact]
    public async Task SimpleOk_WithContentLength_Decodes()
    {
        const string body = "Hello, World!";
        var raw = BuildResponse(200, "OK", body, ("Content-Length", body.Length.ToString()));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Single(responses);
        Assert.Equal((int)HttpStatusCode.OK, (int)responses[0].StatusCode);
        var result = await responses[0].Content.ReadAsStringAsync();
        Assert.Equal("Hello, World!", result);
    }

    [Theory]
    [InlineData(200, "OK", HttpStatusCode.OK)]
    [InlineData(201, "Created", HttpStatusCode.Created)]
    [InlineData(301, "Moved Permanently", HttpStatusCode.MovedPermanently)]
    [InlineData(400, "Bad Request", HttpStatusCode.BadRequest)]
    [InlineData(404, "Not Found", HttpStatusCode.NotFound)]
    [InlineData(500, "Internal Server Error", HttpStatusCode.InternalServerError)]
    public void KnownStatusCodes_ParseCorrectly(int code, string reason, HttpStatusCode expected)
    {
        var raw = BuildResponse(code, reason, "", ("Content-Length", "0"));
        _decoder.TryDecode(raw, out var responses);

        Assert.Equal(expected, responses[0].StatusCode);
        Assert.Equal(reason, responses[0].ReasonPhrase);
    }

    [Theory(DisplayName = "RFC7231-6.1: 2xx status code {code} parsed correctly")]
    [InlineData(200, "OK", HttpStatusCode.OK)]
    [InlineData(201, "Created", HttpStatusCode.Created)]
    [InlineData(202, "Accepted", HttpStatusCode.Accepted)]
    [InlineData(203, "Non-Authoritative Information", HttpStatusCode.NonAuthoritativeInformation)]
    [InlineData(204, "No Content", HttpStatusCode.NoContent)]
    [InlineData(205, "Reset Content", HttpStatusCode.ResetContent)]
    [InlineData(206, "Partial Content", HttpStatusCode.PartialContent)]
    [InlineData(207, "Multi-Status", (HttpStatusCode)207)]
    public void All_2xx_StatusCodes_ParseCorrectly(int code, string reason, HttpStatusCode expected)
    {
        var raw = BuildResponse(code, reason, "", ("Content-Length", "0"));
        _decoder.TryDecode(raw, out var responses);

        Assert.Equal(expected, responses[0].StatusCode);
        Assert.Equal(reason, responses[0].ReasonPhrase);
    }

    [Theory(DisplayName = "RFC7231-6.1: 3xx status code {code} parsed correctly")]
    [InlineData(300, "Multiple Choices", HttpStatusCode.MultipleChoices)]
    [InlineData(301, "Moved Permanently", HttpStatusCode.MovedPermanently)]
    [InlineData(302, "Found", HttpStatusCode.Found)]
    [InlineData(303, "See Other", HttpStatusCode.SeeOther)]
    [InlineData(304, "Not Modified", HttpStatusCode.NotModified)]
    [InlineData(307, "Temporary Redirect", HttpStatusCode.TemporaryRedirect)]
    [InlineData(308, "Permanent Redirect", HttpStatusCode.PermanentRedirect)]
    public void All_3xx_StatusCodes_ParseCorrectly(int code, string reason, HttpStatusCode expected)
    {
        var raw = BuildResponse(code, reason, "", ("Content-Length", "0"));
        _decoder.TryDecode(raw, out var responses);

        Assert.Equal(expected, responses[0].StatusCode);
        Assert.Equal(reason, responses[0].ReasonPhrase);
    }

    [Theory(DisplayName = "RFC7231-6.1: 4xx status code {code} parsed correctly")]
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
    public void All_4xx_StatusCodes_ParseCorrectly(int code, string reason, HttpStatusCode expected)
    {
        var raw = BuildResponse(code, reason, "", ("Content-Length", "0"));
        _decoder.TryDecode(raw, out var responses);

        Assert.Equal(expected, responses[0].StatusCode);
        Assert.Equal(reason, responses[0].ReasonPhrase);
    }

    [Theory(DisplayName = "RFC7231-6.1: 5xx status code {code} parsed correctly")]
    [InlineData(500, "Internal Server Error", HttpStatusCode.InternalServerError)]
    [InlineData(501, "Not Implemented", HttpStatusCode.NotImplemented)]
    [InlineData(502, "Bad Gateway", HttpStatusCode.BadGateway)]
    [InlineData(503, "Service Unavailable", HttpStatusCode.ServiceUnavailable)]
    [InlineData(504, "Gateway Timeout", HttpStatusCode.GatewayTimeout)]
    public void All_5xx_StatusCodes_ParseCorrectly(int code, string reason, HttpStatusCode expected)
    {
        var raw = BuildResponse(code, reason, "", ("Content-Length", "0"));
        _decoder.TryDecode(raw, out var responses);

        Assert.Equal(expected, responses[0].StatusCode);
        Assert.Equal(reason, responses[0].ReasonPhrase);
    }

    [Fact(DisplayName = "RFC7231-6.1: 1xx Informational response has no body")]
    public void Informational_1xx_HasNoBody()
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

    [Theory(DisplayName = "RFC9110: 1xx code {code} parsed with no body")]
    [InlineData(100, "Continue")]
    [InlineData(101, "Switching Protocols")]
    [InlineData(102, "Processing")]
    [InlineData(103, "Early Hints")]
    public void Each_1xx_Code_ParsedWithNoBody(int code, string reason)
    {
        var raw1xx = Encoding.ASCII.GetBytes($"HTTP/1.1 {code} {reason}\r\n\r\n");
        var raw200 = BuildResponse(200, "OK", "data", ("Content-Length", "4"));

        var combined = new byte[raw1xx.Length + raw200.Length];
        raw1xx.CopyTo(combined, 0);
        raw200.Span.CopyTo(combined.AsSpan(raw1xx.Length));

        var decoded = _decoder.TryDecode(combined, out var responses);

        Assert.True(decoded);
        Assert.Single(responses); // 1xx skipped
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
    }

    [Fact(DisplayName = "RFC7231-6.1: 100 Continue before 200 OK decoded correctly")]
    public void Continue_100_Before_200_DecodedCorrectly()
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

    [Fact(DisplayName = "RFC9110: Multiple 1xx interim responses before 200")]
    public async Task Multiple_1xx_Then_200_AllProcessed()
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
        var body = await responses[0].Content.ReadAsStringAsync();
        Assert.Equal("final", body);
    }

    [Fact(DisplayName = "RFC7231-6.1: Custom status code 599 parsed")]
    public void Custom_Status_599_Parsed()
    {
        var raw = "HTTP/1.1 599 Custom\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Equal(599, (int)responses[0].StatusCode);
        Assert.Equal("Custom", responses[0].ReasonPhrase);
    }

    [Fact(DisplayName = "RFC7231-6.1: Status code >599 is a parse error")]
    public void Status_GreaterThan_599_IsError()
    {
        var raw = "HTTP/1.1 600 Invalid\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var ex = Assert.Throws<HttpDecoderException>(() => _decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecodeError.InvalidStatusLine, ex.DecodeError);
    }

    [Fact(DisplayName = "RFC7231-6.1: Empty reason phrase is valid")]
    public void Empty_ReasonPhrase_IsValid()
    {
        var raw = "HTTP/1.1 200 \r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        Assert.True(string.IsNullOrWhiteSpace(responses[0].ReasonPhrase));
    }

    [Fact]
    public void Response204_NoContent_NoBody()
    {
        var raw = BuildResponse(204, "No Content", "", ("Content-Length", "0"));
        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.NoContent, responses[0].StatusCode);
        Assert.Equal(0, responses[0].Content.Headers.ContentLength);
    }

    [Fact]
    public void Informational_100Continue_IsSkipped()
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

    [Fact]
    public void Response304_NoBody_ParsedCorrectly()
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
