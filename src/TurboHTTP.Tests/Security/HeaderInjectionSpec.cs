using System.Text;
using TurboHTTP.Protocol;
using Decoder = TurboHTTP.Protocol.Http11.Decoder;
using Encoder = TurboHTTP.Protocol.Http11.Encoder;

namespace TurboHTTP.Tests.Security;

public sealed class HeaderInjectionSpec
{
    private static string EncodeHttp11(HttpRequestMessage request, int bufferSize = 16384)
    {
        var buffer = new Memory<byte>(new byte[bufferSize]);
        var span = buffer.Span;
        var written = Encoder.Encode(request, ref span);
        return Encoding.ASCII.GetString(buffer.Span[..written]);
    }

    private static void EncodeHttp11Throwing(HttpRequestMessage request)
    {
        var buffer = new Memory<byte>(new byte[8192]);
        var span = buffer.Span;
        Encoder.Encode(request, ref span);
    }

    private static void EncodeHttp10Throwing(HttpRequestMessage request)
    {
        Span<byte> buffer = new byte[8192];
        TurboHTTP.Protocol.Http10.Encoder.Encode(request, ref buffer);
    }

    [Fact(Timeout = 5000)]
    public void Http11Encoder_should_reject_header_name_when_contains_crlf()
    {
        // Attack: Inject CRLF into header name to create additional header lines.
        // "X-Evil\r\nX-Injected" would split into two header lines on the wire.
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("X-Evil\r\nX-Injected", "attack");

        // .NET's HttpRequestHeaders rejects CRLF in header names at the API level,
        // so TryAddWithoutValidation silently drops the header. The encoder never sees it.
        // Note: Contains() throws FormatException for invalid names, so we check via enumeration.
        Assert.DoesNotContain(request.Headers, h => h.Key.Contains("X-Evil"));

        // The request encodes successfully without the malicious header
        var ex = Record.Exception(() => EncodeHttp11Throwing(request));
        Assert.Null(ex);
    }

    [Fact(Timeout = 5000)]
    public void Http11Encoder_should_reject_header_name_when_contains_cr()
    {
        // Attack: Bare CR in header name could cause line splitting in lenient parsers.
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("X-Evil\rInjected", "attack");

        // .NET's HttpRequestHeaders rejects CR in header names
        Assert.DoesNotContain(request.Headers, h => h.Key.Contains("Evil"));
    }

    [Fact(Timeout = 5000)]
    public void Http11Encoder_should_reject_header_name_when_contains_lf()
    {
        // Attack: Bare LF in header name could cause line splitting in lenient parsers.
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("X-Evil\nInjected", "attack");

        // .NET's HttpRequestHeaders rejects LF in header names
        Assert.DoesNotContain(request.Headers, h => h.Key.Contains("Evil"));
    }

    [Fact(Timeout = 5000)]
    public void Http11Encoder_should_reject_header_value_when_contains_crlf_http11()
    {
        // Attack: CRLF in header value creates new header lines.
        // "value\r\nX-Injected: evil" would inject a second header.
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("X-Test", "value\r\nX-Injected: evil");

        Assert.Throws<ArgumentException>(() => EncodeHttp11Throwing(request));
    }

    [Fact(Timeout = 5000)]
    public void Http10Encoder_should_reject_header_value_when_contains_crlf_http10()
    {
        // Same attack vector against HTTP/1.0 encoder
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("X-Test", "value\r\nX-Injected: evil");

        Assert.Throws<ArgumentException>(() => EncodeHttp10Throwing(request));
    }

    [Fact(Timeout = 5000)]
    public void Http11Encoder_should_reject_header_value_when_contains_cr_http11()
    {
        // Attack: Bare CR could be interpreted as line terminator by some proxies.
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("X-Test", "hello\rworld");

        Assert.Throws<ArgumentException>(() => EncodeHttp11Throwing(request));
    }

    [Fact(Timeout = 5000)]
    public void Http11Encoder_should_reject_header_value_when_contains_lf_http11()
    {
        // Attack: Bare LF could be interpreted as line terminator by lenient parsers.
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("X-Test", "hello\nworld");

        Assert.Throws<ArgumentException>(() => EncodeHttp11Throwing(request));
    }

    [Fact(Timeout = 5000)]
    public void Http11Encoder_should_reject_content_header_value_when_contains_crlf_http11()
    {
        // Attack: Injecting CRLF in content headers (e.g., Content-Disposition) to inject additional headers.
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/");
        request.Content = new ByteArrayContent("body"u8.ToArray());
        request.Content.Headers.TryAddWithoutValidation("Content-Disposition", "attachment\r\nX-Injected: evil");

        Assert.Throws<ArgumentException>(() => EncodeHttp11Throwing(request));
    }

    [Fact(Timeout = 5000)]
    public void Http11Encoder_should_reject_header_value_when_contains_nul_http11()
    {
        // Attack: NUL byte can truncate strings in C-based intermediaries,
        // causing the visible value to differ from the transmitted value.
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("X-Test", "safe\0evil");

        Assert.Throws<ArgumentException>(() => EncodeHttp11Throwing(request));
    }

    [Fact(Timeout = 5000)]
    public void Http10Encoder_should_reject_header_value_when_contains_nul_http10()
    {
        // Same NUL truncation attack against HTTP/1.0 encoder
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("X-Test", "safe\0evil");

        Assert.Throws<ArgumentException>(() => EncodeHttp10Throwing(request));
    }

    [Fact(Timeout = 5000)]
    public void Http11Decoder_should_reject_response_when_decoded_header_value_contains_nul()
    {
        // Attack: Malicious server sends NUL byte in header value.
        // The decoder must reject this per RFC 9112 §5.5.
        var decoder = new Decoder();
        var prefix = "HTTP/1.1 200 OK\r\nX-Test: safe"u8.ToArray();
        var nul = new byte[] { 0x00 };
        var suffix = "evil\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var bytes = new byte[prefix.Length + nul.Length + suffix.Length];
        prefix.CopyTo(bytes, 0);
        nul.CopyTo(bytes, prefix.Length);
        suffix.CopyTo(bytes, prefix.Length + nul.Length);

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(bytes, out _));
        Assert.Equal(HttpDecoderError.InvalidFieldValue, ex.DecodeError);
    }

    [Fact(Timeout = 5000)]
    public void Http10Decoder_should_reject_response_when_header_name_contains_space_http10()
    {
        // Attack: Space in header name can cause different parsers to interpret
        // the header name boundary differently (e.g., "Content Length" vs "Content").
        var decoder = new TurboHTTP.Protocol.Http10.Decoder();
        var raw = "HTTP/1.0 200 OK\r\nContent Length: 0\r\n\r\n";
        var bytes = Encoding.ASCII.GetBytes(raw);

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(bytes, out _));
        Assert.Equal(HttpDecoderError.InvalidFieldName, ex.DecodeError);
    }

    [Fact(Timeout = 5000)]
    public void HttpRequestMessage_should_prevent_space_in_header_name_when_adding_via_api()
    {
        // Verify the .NET API itself prevents space-containing header names from reaching the encoder.
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("X Bad Header", "value");

        // .NET rejects header names with spaces
        Assert.DoesNotContain(request.Headers, h => h.Key == "X Bad Header");
    }

    [Fact(Timeout = 5000)]
    public void Http11Decoder_should_reject_response_when_header_value_contains_bare_cr()
    {
        // Attack: Some parsers treat bare CR as a line terminator, which could
        // allow header injection if the upstream proxy accepts bare-CR termination.
        var decoder = new Decoder();

        var prefix = "HTTP/1.1 200 OK\r\nX-Foo: hello"u8.ToArray();
        var bareCr = new byte[] { 0x0D }; // bare CR without LF
        var suffix = "world\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var bytes = new byte[prefix.Length + bareCr.Length + suffix.Length];
        prefix.CopyTo(bytes, 0);
        bareCr.CopyTo(bytes, prefix.Length);
        suffix.CopyTo(bytes, prefix.Length + bareCr.Length);

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(bytes, out _));
        Assert.Equal(HttpDecoderError.InvalidFieldValue, ex.DecodeError);
    }

    [Fact(Timeout = 5000)]
    public void Http11Encoder_should_reject_request_when_header_value_contains_bare_cr()
    {
        // Attack: Ensure the encoder also prevents bare CR from being emitted on the wire.
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("X-Test", "hello\rworld");

        var ex = Assert.Throws<ArgumentException>(() => EncodeHttp11Throwing(request));
        Assert.Contains("X-Test", ex.Message);
    }

    [Fact(Timeout = 5000)]
    public void Http11Decoder_should_reject_response_when_transfer_encoding_and_content_length_both_present()
    {
        // Attack: CL-TE desync — a reverse proxy uses Content-Length to determine
        // body boundary while the backend uses Transfer-Encoding: chunked.
        var decoder = new Decoder();
        const string response =
            "HTTP/1.1 200 OK\r\n" +
            "Transfer-Encoding: chunked\r\n" +
            "Content-Length: 5\r\n" +
            "\r\n" +
            "5\r\nHello\r\n0\r\n\r\n";
        var raw = Encoding.ASCII.GetBytes(response);

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecoderError.ChunkedWithContentLength, ex.DecodeError);
    }

    [Fact(Timeout = 5000)]
    public void Http11Decoder_should_reject_response_when_content_length_before_transfer_encoding()
    {
        // Attack: Same desync but with headers in reversed order.
        var decoder = new Decoder();
        const string response =
            "HTTP/1.1 200 OK\r\n" +
            "Content-Length: 5\r\n" +
            "Transfer-Encoding: chunked\r\n" +
            "\r\n" +
            "5\r\nHello\r\n0\r\n\r\n";
        var raw = Encoding.ASCII.GetBytes(response);

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecoderError.ChunkedWithContentLength, ex.DecodeError);
    }

    [Fact(Timeout = 5000)]
    public void Http11Decoder_should_reject_response_when_chunked_with_content_length_zero()
    {
        // Attack: Even Content-Length: 0 with Transfer-Encoding: chunked is ambiguous.
        var decoder = new Decoder();
        const string response =
            "HTTP/1.1 200 OK\r\n" +
            "Transfer-Encoding: chunked\r\n" +
            "Content-Length: 0\r\n" +
            "\r\n" +
            "5\r\nHello\r\n0\r\n\r\n";
        var raw = Encoding.ASCII.GetBytes(response);

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecoderError.ChunkedWithContentLength, ex.DecodeError);
    }

    [Fact(Timeout = 5000)]
    public void Http11Decoder_should_reject_response_when_duplicate_content_length_different_values()
    {
        // Attack: Two Content-Length headers with different values. A front-end proxy
        // might use the first (5), while the backend uses the second (10).
        var decoder = new Decoder();
        const string response =
            "HTTP/1.1 200 OK\r\n" +
            "Content-Length: 5\r\n" +
            "Content-Length: 10\r\n" +
            "\r\n" +
            "Hello";
        var raw = Encoding.ASCII.GetBytes(response);

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecoderError.MultipleContentLengthValues, ex.DecodeError);
    }

    [Fact(Timeout = 5000)]
    public async Task Http11Decoder_should_accept_response_when_duplicate_content_length_same_values()
    {
        // Non-attack: Duplicate Content-Length with identical values is safe per RFC 9112 §6.3.
        var decoder = new Decoder();
        const string response =
            "HTTP/1.1 200 OK\r\n" +
            "Content-Length: 5\r\n" +
            "Content-Length: 5\r\n" +
            "\r\n" +
            "Hello";
        var raw = Encoding.ASCII.GetBytes(response);

        var decoded = decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Single(responses);
        var body = await responses[0].Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Hello"u8.ToArray(), body);
    }

    [Fact(Timeout = 5000)]
    public void Http11Decoder_should_reject_response_when_three_conflicting_content_length_values()
    {
        // Attack: Three Content-Length headers where only the last differs.
        var decoder = new Decoder();
        const string response =
            "HTTP/1.1 200 OK\r\n" +
            "Content-Length: 5\r\n" +
            "Content-Length: 5\r\n" +
            "Content-Length: 10\r\n" +
            "\r\n" +
            "Hello";
        var raw = Encoding.ASCII.GetBytes(response);

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecoderError.MultipleContentLengthValues, ex.DecodeError);
    }

    [Fact(Timeout = 5000)]
    public void Http11Encoder_should_omit_content_length_when_transfer_encoding_chunked_is_set()
    {
        // Verify the encoder does not emit both Transfer-Encoding and Content-Length,
        // which would create an ambiguous message exploitable for request smuggling.
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/");
        request.Content = new ByteArrayContent("Hello"u8.ToArray());
        request.Content.Headers.ContentLength = 5;
        request.Headers.TransferEncodingChunked = true;

        var output = EncodeHttp11(request);

        Assert.Contains("Transfer-Encoding", output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Content-Length", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Timeout = 5000)]
    public void Http11Encoder_should_filter_connection_specific_headers_when_encoding()
    {
        // Hop-by-hop headers like Keep-Alive, Upgrade, Proxy-Connection must not
        // be forwarded. If emitted, they could confuse intermediate proxies.
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("Keep-Alive", "timeout=5");
        request.Headers.TryAddWithoutValidation("Proxy-Connection", "keep-alive");

        var output = EncodeHttp11(request);

        // Check that no header line starts with these names.
        // "keep-alive" may appear as a Connection header value, which is valid.
        Assert.DoesNotContain("Keep-Alive:", output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Proxy-Connection:", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Timeout = 5000)]
    public void Http11Encoder_should_strip_chunked_from_te_header_when_encoding()
    {
        // RFC 9112 §7.4: TE header MUST NOT include "chunked".
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("TE", "trailers, chunked");

        var output = EncodeHttp11(request);

        Assert.Contains("TE: trailers", output);
        // The TE header should not contain "chunked"
        var teLineStart = output.IndexOf("TE:", StringComparison.Ordinal);
        var teLineEnd = output.IndexOf("\r\n", teLineStart, StringComparison.Ordinal);
        var teLine = output[teLineStart..teLineEnd];
        Assert.DoesNotContain("chunked", teLine, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Timeout = 5000)]
    public void Http11Encoder_should_not_emit_bare_cr_or_lf_when_encoding_normal_request()
    {
        // Verify the encoded output uses only CRLF line terminators, never bare CR or LF.
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/path?query=value");
        request.Headers.TryAddWithoutValidation("X-Custom", "safe-value");
        request.Headers.TryAddWithoutValidation("Accept", "text/html");

        var buffer = new Memory<byte>(new byte[16384]);
        var span = buffer.Span;
        var bytesWritten = Encoder.Encode(request, ref span);

        var output = buffer.Span[..bytesWritten];

        // Check every byte: any CR must be immediately followed by LF
        for (var i = 0; i < output.Length; i++)
        {
            if (output[i] == 0x0D) // CR
            {
                Assert.True(i + 1 < output.Length && output[i + 1] == 0x0A,
                    $"Bare CR found at position {i} without following LF");
            }
            else if (output[i] == 0x0A) // LF
            {
                Assert.True(i > 0 && output[i - 1] == 0x0D,
                    $"Bare LF found at position {i} without preceding CR");
            }
        }
    }

    [Fact(Timeout = 5000)]
    public void Http11Encoder_should_not_throw_when_header_values_are_legitimate()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("X-Request-Id", "abc-123-def-456");
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer eyJhbGciOiJIUzI1NiJ9.token.sig");

        var ex = Record.Exception(() => EncodeHttp11Throwing(request));
        Assert.Null(ex);
    }

    [Fact(Timeout = 5000)]
    public void Http11Encoder_should_not_throw_when_header_values_contain_safe_special_chars()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("Cookie", "session=abc; path=/; domain=.example.com");
        request.Headers.TryAddWithoutValidation("Accept", "text/html; charset=utf-8");

        var ex = Record.Exception(() => EncodeHttp11Throwing(request));
        Assert.Null(ex);
    }
}