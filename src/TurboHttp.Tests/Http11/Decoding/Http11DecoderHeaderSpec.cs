using System.Text;
using TurboHttp.Protocol;
using TurboHttp.Protocol.Http11;

namespace TurboHttp.Tests.Http11.Decoding;

/// <summary>
/// Tests HTTP/1.1 response header field parsing per RFC 9112 §5.
/// Verifies header name/value extraction, multi-value handling, and OWS trimming.
/// </summary>
/// <remarks>
/// Class under test: <see cref="Http11Decoder"/>.
/// RFC 9112 §5: Header fields — field-name ":" OWS field-value OWS CRLF.
/// </remarks>
public sealed class Http11DecoderHeaderSpec
{
    private readonly Http11Decoder _decoder = new();

    [Fact]
    [Trait("RFC", "RFC9112-5")]
    public void Http11Decoder_should_preserve_headers_when_custom_headers_present()
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
    [Trait("RFC", "RFC9112-5")]
    public void Http11Decoder_should_throw_http_decoder_exception_when_header_without_colon()
    {
        // RFC 9112 §5.1 / RFC 7230 §3.2: every header field MUST contain a colon separator.
        // A header line with no colon is a protocol violation and MUST be rejected.
        var raw = "HTTP/1.1 200 OK\r\nThisHeaderHasNoColon\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var ex = Assert.Throws<HttpDecoderException>(() => _decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecoderError.InvalidHeader, ex.DecodeError);
    }

    [Fact]
    [Trait("RFC", "RFC9112-5")]
    public void Http11Decoder_should_parse_header_field_when_standard_format()
    {
        var raw = "HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Equal("text/plain", responses[0].Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    [Trait("RFC", "RFC9112-5")]
    public void Http11Decoder_should_trim_ows_when_header_value_has_whitespace()
    {
        var raw = "HTTP/1.1 200 OK\r\nX-Foo:   bar   \r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.True(responses[0].Headers.TryGetValues("X-Foo", out var values));
        Assert.Equal("bar", values.Single());
    }

    [Fact]
    [Trait("RFC", "RFC9112-5")]
    public void Http11Decoder_should_accept_header_when_empty_value()
    {
        var raw = "HTTP/1.1 200 OK\r\nX-Empty:\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.True(responses[0].Headers.TryGetValues("X-Empty", out var values));
        Assert.Equal("", values.Single());
    }

    [Fact]
    [Trait("RFC", "RFC9112-5")]
    public void Http11Decoder_should_preserve_headers_when_multiple_same_name()
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

    [Fact]
    [Trait("RFC", "RFC9112-5")]
    public void Http11Decoder_should_reject_obs_fold_when_http11()
    {
        var raw = "HTTP/1.1 200 OK\r\nX-Foo: bar\r\n baz\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        Assert.Throws<HttpDecoderException>(() => _decoder.TryDecode(raw, out _));
    }

    [Fact]
    [Trait("RFC", "RFC9112-5")]
    public void Http11Decoder_should_error_when_header_without_colon()
    {
        var raw = "HTTP/1.1 200 OK\r\nThisHeaderHasNoColon\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var ex = Assert.Throws<HttpDecoderException>(() => _decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecoderError.InvalidHeader, ex.DecodeError);
    }

    [Fact]
    [Trait("RFC", "RFC9112-5")]
    public void Http11Decoder_should_lookup_case_insensitively_when_header_name()
    {
        var raw = "HTTP/1.1 200 OK\r\nHOST: example.com\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.True(responses[0].Headers.TryGetValues("Host", out var values));
        Assert.Equal("example.com", values.Single());
    }

    [Fact]
    [Trait("RFC", "RFC9112-5")]
    public void Http11Decoder_should_accept_tab_when_in_header_value()
    {
        var raw = "HTTP/1.1 200 OK\r\nX-Tab: before\ttab\tafter\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.True(responses[0].Headers.TryGetValues("X-Tab", out var values));
        Assert.Equal("before\ttab\tafter", values.Single());
    }

    [Fact]
    [Trait("RFC", "RFC9112-5")]
    public void Http11Decoder_should_parse_header_value_when_quoted_string()
    {
        var raw = "HTTP/1.1 200 OK\r\nX-Quoted: \"quoted value\"\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.True(responses[0].Headers.TryGetValues("X-Quoted", out var values));
        Assert.Equal("\"quoted value\"", values.Single());
    }

    [Fact]
    [Trait("RFC", "RFC9112-5")]
    public void Http11Decoder_should_parse_parameters_when_content_type_header()
    {
        var raw = "HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Equal("text/html", responses[0].Content.Headers.ContentType?.MediaType);
        Assert.Equal("utf-8", responses[0].Content.Headers.ContentType?.CharSet);
    }

    [Fact]
    [Trait("RFC", "RFC9112-5")]
    public void Http11Decoder_should_trim_ows_when_header_decoded()
    {
        // RFC 7230 §3.2: OWS (optional whitespace) around header field value MUST be trimmed.
        var raw = "HTTP/1.1 200 OK\r\nX-Foo:   bar   \r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.True(responses[0].Headers.TryGetValues("X-Foo", out var values));
        Assert.Equal("bar", values.Single());
    }

    [Fact]
    [Trait("RFC", "RFC9112-5")]
    public void Http11Decoder_should_accept_empty_value_when_header_decoded()
    {
        // RFC 7230 §3.2: A header field with an empty value is valid.
        var raw = "HTTP/1.1 200 OK\r\nX-Empty:\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.True(responses[0].Headers.TryGetValues("X-Empty", out var values));
        Assert.Equal("", values.Single());
    }

    [Fact]
    [Trait("RFC", "RFC9112-5")]
    public void Http11Decoder_should_match_case_insensitively_when_header_name_decoded()
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
    [Trait("RFC", "RFC9112-5")]
    public void Http11Decoder_should_preserve_multiple_values_when_same_header_name()
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
    [Trait("RFC", "RFC9112-5")]
    public void Http11Decoder_should_reject_obs_fold_when_http11_header()
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
