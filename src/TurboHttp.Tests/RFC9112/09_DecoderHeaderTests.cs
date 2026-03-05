using System.Text;
using TurboHttp.Protocol;

namespace TurboHttp.Tests.RFC9112;

public sealed class Http11DecoderHeaderTests
{
    private readonly Http11Decoder _decoder = new();

    [Fact]
    public void ResponseWithCustomHeaders_PreservesHeaders()
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
    public void Decode_HeaderWithoutColon_ThrowsHttpDecoderException()
    {
        // RFC 9112 §5.1 / RFC 7230 §3.2: every header field MUST contain a colon separator.
        // A header line with no colon is a protocol violation and MUST be rejected.
        var raw = "HTTP/1.1 200 OK\r\nThisHeaderHasNoColon\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var ex = Assert.Throws<HttpDecoderException>(() => _decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecodeError.InvalidHeader, ex.DecodeError);
    }

    [Fact(DisplayName = "RFC7230-3.2: Standard header field Name: value parsed")]
    public void Standard_HeaderField_Parsed()
    {
        var raw = "HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Equal("text/plain", responses[0].Content.Headers.ContentType?.MediaType);
    }

    [Fact(DisplayName = "RFC7230-3.2: OWS trimmed from header value")]
    public void OWS_TrimmedFromHeaderValue()
    {
        var raw = "HTTP/1.1 200 OK\r\nX-Foo:   bar   \r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.True(responses[0].Headers.TryGetValues("X-Foo", out var values));
        Assert.Equal("bar", values.Single());
    }

    [Fact(DisplayName = "RFC7230-3.2: Empty header value accepted")]
    public void Empty_HeaderValue_Accepted()
    {
        var raw = "HTTP/1.1 200 OK\r\nX-Empty:\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.True(responses[0].Headers.TryGetValues("X-Empty", out var values));
        Assert.Equal("", values.Single());
    }

    [Fact(DisplayName = "RFC7230-3.2: Multiple same-name headers both accessible")]
    public void Multiple_SameName_Headers_Preserved()
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

    [Fact(DisplayName = "RFC7230-3.2: Obs-fold rejected in HTTP/1.1")]
    public void ObsFold_RejectedInHttp11()
    {
        var raw = "HTTP/1.1 200 OK\r\nX-Foo: bar\r\n baz\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        Assert.Throws<HttpDecoderException>(() => _decoder.TryDecode(raw, out _));
    }

    [Fact(DisplayName = "RFC7230-3.2: Header without colon is parse error")]
    public void Header_WithoutColon_IsError()
    {
        var raw = "HTTP/1.1 200 OK\r\nThisHeaderHasNoColon\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var ex = Assert.Throws<HttpDecoderException>(() => _decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecodeError.InvalidHeader, ex.DecodeError);
    }

    [Fact(DisplayName = "RFC7230-3.2: Header name lookup case-insensitive")]
    public void HeaderName_Lookup_CaseInsensitive()
    {
        var raw = "HTTP/1.1 200 OK\r\nHOST: example.com\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.True(responses[0].Headers.TryGetValues("Host", out var values));
        Assert.Equal("example.com", values.Single());
    }

    [Fact(DisplayName = "RFC9112-3.2: Tab character in header value accepted")]
    public void Tab_InHeaderValue_Accepted()
    {
        var raw = "HTTP/1.1 200 OK\r\nX-Tab: before\ttab\tafter\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.True(responses[0].Headers.TryGetValues("X-Tab", out var values));
        Assert.Equal("before\ttab\tafter", values.Single());
    }

    [Fact(DisplayName = "RFC9112-3.2: Quoted-string header value parsed")]
    public void QuotedString_HeaderValue_Parsed()
    {
        var raw = "HTTP/1.1 200 OK\r\nX-Quoted: \"quoted value\"\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.True(responses[0].Headers.TryGetValues("X-Quoted", out var values));
        Assert.Equal("\"quoted value\"", values.Single());
    }

    [Fact(DisplayName = "RFC9112-3.2: Content-Type: text/html; charset=utf-8 accessible")]
    public void ContentType_WithParameters_Parsed()
    {
        var raw = "HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Equal("text/html", responses[0].Content.Headers.ContentType?.MediaType);
        Assert.Equal("utf-8", responses[0].Content.Headers.ContentType?.CharSet);
    }

    [Fact]
    public void Decode_Header_OWS_Trimmed()
    {
        // RFC 7230 §3.2: OWS (optional whitespace) around header field value MUST be trimmed.
        var raw = "HTTP/1.1 200 OK\r\nX-Foo:   bar   \r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.True(responses[0].Headers.TryGetValues("X-Foo", out var values));
        Assert.Equal("bar", values.Single());
    }

    [Fact]
    public void Decode_Header_EmptyValue_Accepted()
    {
        // RFC 7230 §3.2: A header field with an empty value is valid.
        var raw = "HTTP/1.1 200 OK\r\nX-Empty:\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.True(responses[0].Headers.TryGetValues("X-Empty", out var values));
        Assert.Equal("", values.Single());
    }

    [Fact]
    public void Decode_Header_CaseInsensitiveName()
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
    public void Decode_Header_MultipleValuesForSameName_Preserved()
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
    public void Decode_Header_ObsFold_Http11_IsError()
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
