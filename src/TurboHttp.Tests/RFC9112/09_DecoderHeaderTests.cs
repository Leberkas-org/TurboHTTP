using System.Text;
using TurboHttp.Protocol;
using TurboHttp.Protocol.RFC9112;

namespace TurboHttp.Tests.RFC9112;

/// <summary>
/// Tests HTTP/1.1 response header field parsing per RFC 9112 §5.
/// Verifies header name/value extraction, multi-value handling, and OWS trimming.
/// </summary>
/// <remarks>
/// Class under test: <see cref="Http11Decoder"/>.
/// RFC 9112 §5: Header fields — field-name ":" OWS field-value OWS CRLF.
/// </remarks>
public sealed class Http11DecoderHeaderTests
{
    private readonly Http11Decoder _decoder = new();

    [Fact]
    public void Should_PreserveHeaders_When_CustomHeadersPresent()
    {
        var raw = BuildResponse(200, "OK", "data",
            ("Content-Length", "4"),
            ("X-Custom", "my-value"),
            ("Cache-Control", "no-store"));

        _decoder.TryDecode(raw, out var responses);

        Assert.True(responses[0].Headers.TryGetValues("X-Custom", out var custom));
        Assert.Equal("my-value", custom.Single());
        Assert.True(responses[0].Headers.TryGetValues("Cache-Control", out var cache));
        Assert.Equal("no-store", cache.Single());
    }

    [Fact]
    public void Should_ThrowHttpDecoderException_When_HeaderWithoutColon()
    {
        // RFC 9112 §5.1 / RFC 7230 §3.2: every header field MUST contain a colon separator.
        // A header line with no colon is a protocol violation and MUST be rejected.
        var raw = "HTTP/1.1 200 OK\r\nThisHeaderHasNoColon\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var ex = Assert.Throws<HttpDecoderException>(() => _decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecoderError.InvalidHeader, ex.DecodeError);
    }

    [Fact(DisplayName = "RFC9112-5-HD-001: Standard header field Name: value parsed")]
    public void Should_ParseHeaderField_When_StandardFormat()
    {
        var raw = "HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Equal("text/plain", responses[0].Content.Headers.ContentType?.MediaType);
    }

    [Fact(DisplayName = "RFC9112-5-HD-002: OWS trimmed from header value")]
    public void Should_TrimOWS_When_HeaderValueHasWhitespace()
    {
        var raw = "HTTP/1.1 200 OK\r\nX-Foo:   bar   \r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.True(responses[0].Headers.TryGetValues("X-Foo", out var values));
        Assert.Equal("bar", values.Single());
    }

    [Fact(DisplayName = "RFC9112-5-HD-003: Empty header value accepted")]
    public void Should_AcceptHeader_When_EmptyValue()
    {
        var raw = "HTTP/1.1 200 OK\r\nX-Empty:\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.True(responses[0].Headers.TryGetValues("X-Empty", out var values));
        Assert.Equal("", values.Single());
    }

    [Fact(DisplayName = "RFC9112-5-HD-004: Multiple same-name headers both accessible")]
    public void Should_PreserveHeaders_When_MultipleSameName()
    {
        var raw = "HTTP/1.1 200 OK\r\nAccept: text/html\r\nAccept: application/json\r\nContent-Length: 0\r\n\r\n"u8
            .ToArray();

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.True(responses[0].Headers.TryGetValues("Accept", out var values));
        var list = values.ToList();
        Assert.Contains("text/html", list);
        Assert.Contains("application/json", list);
    }

    [Fact(DisplayName = "RFC9112-5-HD-005: Obs-fold rejected in HTTP/1.1")]
    public void Should_RejectObsFold_When_Http11()
    {
        var raw = "HTTP/1.1 200 OK\r\nX-Foo: bar\r\n baz\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        Assert.Throws<HttpDecoderException>(() => _decoder.TryDecode(raw, out _));
    }

    [Fact(DisplayName = "RFC9112-5-HD-006: Header without colon is parse error")]
    public void Should_Error_When_HeaderWithoutColon()
    {
        var raw = "HTTP/1.1 200 OK\r\nThisHeaderHasNoColon\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var ex = Assert.Throws<HttpDecoderException>(() => _decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecoderError.InvalidHeader, ex.DecodeError);
    }

    [Fact(DisplayName = "RFC9112-5-HD-007: Header name lookup case-insensitive")]
    public void Should_LookupCaseInsensitively_When_HeaderName()
    {
        var raw = "HTTP/1.1 200 OK\r\nHOST: example.com\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.True(responses[0].Headers.TryGetValues("Host", out var values));
        Assert.Equal("example.com", values.Single());
    }

    [Fact(DisplayName = "RFC9112-5-HD-008: Tab character in header value accepted")]
    public void Should_AcceptTab_When_InHeaderValue()
    {
        var raw = "HTTP/1.1 200 OK\r\nX-Tab: before\ttab\tafter\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.True(responses[0].Headers.TryGetValues("X-Tab", out var values));
        Assert.Equal("before\ttab\tafter", values.Single());
    }

    [Fact(DisplayName = "RFC9112-5-HD-009: Quoted-string header value parsed")]
    public void Should_ParseHeaderValue_When_QuotedString()
    {
        var raw = "HTTP/1.1 200 OK\r\nX-Quoted: \"quoted value\"\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.True(responses[0].Headers.TryGetValues("X-Quoted", out var values));
        Assert.Equal("\"quoted value\"", values.Single());
    }

    [Fact(DisplayName = "RFC9112-5-HD-010: Content-Type: text/html; charset=utf-8 accessible")]
    public void Should_ParseParameters_When_ContentTypeHeader()
    {
        var raw = "HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Equal("text/html", responses[0].Content.Headers.ContentType?.MediaType);
        Assert.Equal("utf-8", responses[0].Content.Headers.ContentType?.CharSet);
    }

    [Fact]
    public void Should_TrimOWS_When_HeaderDecoded()
    {
        // RFC 7230 §3.2: OWS (optional whitespace) around header field value MUST be trimmed.
        var raw = "HTTP/1.1 200 OK\r\nX-Foo:   bar   \r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.True(responses[0].Headers.TryGetValues("X-Foo", out var values));
        Assert.Equal("bar", values.Single());
    }

    [Fact]
    public void Should_AcceptEmptyValue_When_HeaderDecoded()
    {
        // RFC 7230 §3.2: A header field with an empty value is valid.
        var raw = "HTTP/1.1 200 OK\r\nX-Empty:\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.True(responses[0].Headers.TryGetValues("X-Empty", out var values));
        Assert.Equal("", values.Single());
    }

    [Fact]
    public void Should_MatchCaseInsensitively_When_HeaderNameDecoded()
    {
        // RFC 7230 §3.2: Header field names are case-insensitive.
        var raw = "HTTP/1.1 200 OK\r\nHOST: example.com\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        // Accessible via any casing — .NET HttpResponseMessage headers are case-insensitive
        Assert.True(responses[0].Headers.TryGetValues("Host", out var values));
        Assert.Equal("example.com", values.Single());
    }

    [Fact]
    public void Should_PreserveMultipleValues_When_SameHeaderName()
    {
        // RFC 7230 §3.2.2: Multiple header fields with the same name are valid;
        // the recipient MUST preserve all values.
        var raw = "HTTP/1.1 200 OK\r\nAccept: text/html\r\nAccept: application/json\r\nContent-Length: 0\r\n\r\n"u8
            .ToArray();

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.True(responses[0].Headers.TryGetValues("Accept", out var values));
        var list = values.ToList();
        Assert.Contains("text/html", list);
        Assert.Contains("application/json", list);
    }

    [Fact]
    public void Should_RejectObsFold_When_Http11Header()
    {
        // RFC 9112 §5.2: A server MUST NOT send obs-fold in HTTP/1.1 responses.
        var raw = "HTTP/1.1 200 OK\r\nX-Foo: bar\r\n baz\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        Assert.Throws<HttpDecoderException>(() => _decoder.TryDecode(raw, out _));
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
