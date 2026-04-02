using System.Text;
using TurboHttp.Protocol;
using TurboHttp.Protocol.Http11;

namespace TurboHttp.Tests.Http11;

/// <summary>
/// Tests rejection of malformed HTTP/1.1 responses per RFC 9112.
/// Verifies that invalid status lines, headers, and versions produce appropriate errors.
/// </summary>
/// <remarks>
/// Class under test: <see cref="Http11Decoder"/>.
/// RFC 9112 §4: Malformed status-line or unsupported HTTP-version must be rejected.
/// </remarks>
public sealed class Http11NegativePathSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    public void Http11NegativePath_should_reject_status_line_when_http20_version()
    {
        // RFC 9112 §4: status-line = HTTP-version SP status-code SP reason-phrase CRLF
        // HTTP-version must be "HTTP/1.1" or "HTTP/1.0"; "HTTP/2.0" is not a valid HTTP/1.1 status line.
        var decoder = new Http11Decoder();
        var raw = "HTTP/2.0 200 OK\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecoderError.InvalidStatusLine, ex.DecodeError);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    public void Http11NegativePath_should_reject_status_line_when_non_http_protocol()
    {
        // "HTTPS/1.1" is not a valid HTTP-version token.
        var decoder = new Http11Decoder();
        var raw = "HTTPS/1.1 200 OK\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecoderError.InvalidStatusLine, ex.DecodeError);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    public void Http11NegativePath_should_reject_status_line_when_double_space_before_status_code()
    {
        // RFC 9112 §4: exactly one SP between HTTP-version and 3-digit status code.
        // "HTTP/1.1  200 OK" has a leading space before the status digits, making it unparseable.
        var decoder = new Http11Decoder();
        var raw = "HTTP/1.1  200 OK\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecoderError.InvalidStatusLine, ex.DecodeError);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    public void Http11NegativePath_should_reject_status_line_when_two_digit_status_code()
    {
        // RFC 9112 §4: status-code is exactly 3 decimal digits.
        var decoder = new Http11Decoder();
        var raw = "HTTP/1.1 20 OK\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecoderError.InvalidStatusLine, ex.DecodeError);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    public void Http11NegativePath_should_reject_status_line_when_non_digit_in_status_code()
    {
        // Status code must be exactly 3 ASCII digits.
        var decoder = new Http11Decoder();
        var raw = "HTTP/1.1 20A OK\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecoderError.InvalidStatusLine, ex.DecodeError);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    public void Http11NegativePath_should_never_decode_when_bare_line_feed_in_status_line()
    {
        // RFC 9112 §2.2: a recipient MUST NOT treat a bare LF as a line terminator.
        // Our decoder uses strict CRLF matching; bare-LF input is treated as incomplete data.
        var decoder = new Http11Decoder();
        var raw = "HTTP/1.1 200 OK\nContent-Length: 0\n\n"u8.ToArray();

        var decoded = decoder.TryDecode(raw, out var responses);

        Assert.False(decoded, "Bare-LF response should not be decoded (no valid CRLF terminator found).");
        Assert.Empty(responses);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    public void Http11NegativePath_should_catch_by_header_limit_when_overlong_reason_phrase()
    {
        // The 64 KB total header section size guard also protects against overlong status lines.
        // A reason phrase that makes the entire header block exceed 64 KB is rejected.
        var decoder = new Http11Decoder(); // default 64 KB total header limit
        var longReason = new string('X', 66000);
        var raw = Encoding.ASCII.GetBytes($"HTTP/1.1 200 {longReason}\r\nContent-Length: 0\r\n\r\n");

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecoderError.TotalHeadersTooLarge, ex.DecodeError);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-5")]
    public void Http11NegativePath_should_reject_trailer_when_chunked_trailer_without_colon()
    {
        // RFC 9112 §7.1.2: trailer-field = field-line; each field-line MUST have a colon.
        // A trailer field with no colon delimiter is a parse error.
        var decoder = new Http11Decoder();

        // Chunked body: one chunk "Hello", then last chunk (0), then a malformed trailer.
        const string response =
            "HTTP/1.1 200 OK\r\n" +
            "Transfer-Encoding: chunked\r\n" +
            "\r\n" +
            "5\r\nHello\r\n" +
            "0\r\n" +
            "InvalidTrailerNoColon\r\n" + // no colon — invalid
            "\r\n";
        var raw = Encoding.ASCII.GetBytes(response);

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecoderError.InvalidHeader, ex.DecodeError);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-5")]
    public void Http11NegativePath_should_reject_trailer_when_empty_field_name()
    {
        // ": value" — colonIdx == 0 means empty field name, which is invalid per RFC 9112 §5.1.
        var decoder = new Http11Decoder();

        const string response =
            "HTTP/1.1 200 OK\r\n" +
            "Transfer-Encoding: chunked\r\n" +
            "\r\n" +
            "5\r\nHello\r\n" +
            "0\r\n" +
            ": EmptyName\r\n" + // empty field name
            "\r\n";
        var raw = Encoding.ASCII.GetBytes(response);

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecoderError.InvalidHeader, ex.DecodeError);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public void Http11NegativePath_should_yield_empty_body_when_non_chunked_without_content_length()
    {
        // When Transfer-Encoding is present but is not "chunked" (e.g., "gzip"),
        // and there is no Content-Length header, the decoder cannot determine body length.
        // Per RFC 9112 §6.3 rule 7: the message body length is determined by the number
        // of octets received prior to the server closing the connection.
        // In the absence of Content-Length, the decoder returns an empty body (connection-close framing
        // is handled at the I/O layer, not the protocol layer).
        var decoder = new Http11Decoder();
        var raw = Encoding.ASCII.GetBytes(
            "HTTP/1.1 200 OK\r\n" +
            "Transfer-Encoding: gzip\r\n" +
            "\r\n");

        var decoded = decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Single(responses);
        Assert.Equal(0, responses[0].Content.Headers.ContentLength ?? 0);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public async Task Http11NegativePath_should_treat_as_pipelined_response_when_bytes_after_content_length()
    {
        // RFC 9112 §6.3: content length terminates the body exactly.
        // Extra bytes following the declared body must be treated as the next pipelined response,
        // not as part of the current response body.
        var decoder = new Http11Decoder();

        const string twoResponses =
            "HTTP/1.1 200 OK\r\n" +
            "Content-Length: 5\r\n" +
            "\r\n" +
            "Hello" + // exactly 5 bytes
            "HTTP/1.1 200 OK\r\n" +
            "Content-Length: 3\r\n" +
            "\r\n" +
            "Bye";
        var raw = Encoding.ASCII.GetBytes(twoResponses);

        var decoded = decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Equal(2, responses.Count);

        var body1 = await responses[0].Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        var body2 = await responses[1].Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Hello"u8.ToArray(), body1);
        Assert.Equal("Bye"u8.ToArray(), body2);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15")]
    public async Task Http11NegativePath_should_have_empty_body_when_response_204()
    {
        // RFC 9110 §15.3.5: A 204 response MUST NOT include a message body.
        // The decoder must return an empty body even if Content-Length is present in headers.
        var decoder = new Http11Decoder();
        var raw = "HTTP/1.1 204 No Content\r\nContent-Length: 10\r\n\r\n"u8.ToArray();

        var decoded = decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Single(responses);
        Assert.Equal(System.Net.HttpStatusCode.NoContent, responses[0].StatusCode);

        var body = await responses[0].Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Empty(body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15")]
    public async Task Http11NegativePath_should_have_empty_body_when_response_304()
    {
        // RFC 9110 §15.4.5: A 304 response MUST NOT contain a message body.
        var decoder = new Http11Decoder();
        var raw = "HTTP/1.1 304 Not Modified\r\nContent-Length: 20\r\nETag: \"abc\"\r\n\r\n"u8.ToArray();

        var decoded = decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Single(responses);
        Assert.Equal(System.Net.HttpStatusCode.NotModified, responses[0].StatusCode);

        var body = await responses[0].Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Empty(body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9")]
    public async Task Http11NegativePath_should_accept_when_multiple_content_length_same_value()
    {
        // RFC 9112 §6.3: If a message is received with multiple Content-Length header fields
        // with identical values, the recipient MAY treat the message as having a single value.
        // This is NOT a smuggling scenario (different values are the attack vector).
        var decoder = new Http11Decoder();
        const string response =
            "HTTP/1.1 200 OK\r\n" +
            "Content-Length: 5\r\n" +
            "Content-Length: 5\r\n" + // duplicate, same value
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
    [Trait("RFC", "RFC9112-9")]
    public void Http11NegativePath_should_reject_when_multiple_content_length_different_values()
    {
        // RFC 9112 §6.3: If values differ, the recipient MUST reject the message.
        // This prevents HTTP request smuggling via Content-Length ambiguity.
        var decoder = new Http11Decoder();
        const string response =
            "HTTP/1.1 200 OK\r\n" +
            "Content-Length: 5\r\n" +
            "Content-Length: 10\r\n" + // different value — smuggling attempt
            "\r\n" +
            "Hello";
        var raw = Encoding.ASCII.GetBytes(response);

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecoderError.MultipleContentLengthValues, ex.DecodeError);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9")]
    public void Http11NegativePath_should_reject_when_transfer_encoding_and_content_length()
    {
        // RFC 9112 §6.3: If Transfer-Encoding and Content-Length are both present,
        // Transfer-Encoding supersedes, and the recipient SHOULD reject the message.
        // This guards against TE/CL desync smuggling attacks.
        var decoder = new Http11Decoder();
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
    [Trait("RFC", "RFC9112-7")]
    public void Http11NegativePath_should_reject_when_chunked_zero_size_non_numeric_characters()
    {
        // RFC 9112 §7.1: chunk-size = 1*HEXDIG; "0x5" uses non-hex prefix "0x" which is invalid.
        var decoder = new Http11Decoder();
        const string response =
            "HTTP/1.1 200 OK\r\n" +
            "Transfer-Encoding: chunked\r\n" +
            "\r\n" +
            "0x5\r\nHello\r\n" + // "0x5" is not valid HEXDIG (the 'x' makes it invalid)
            "0\r\n\r\n";
        var raw = Encoding.ASCII.GetBytes(response);

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecoderError.InvalidChunkSize, ex.DecodeError);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7")]
    public async Task Http11NegativePath_should_accept_when_chunked_upper_case_hex_size()
    {
        // RFC 9112 §7.1: chunk-size = 1*HEXDIG; HEXDIG includes both upper and lower case A-F.
        var decoder = new Http11Decoder();
        const string response =
            "HTTP/1.1 200 OK\r\n" +
            "Transfer-Encoding: chunked\r\n" +
            "\r\n" +
            "A\r\n0123456789\r\n" + // 10 bytes (0xA = 10)
            "0\r\n\r\n";
        var raw = Encoding.ASCII.GetBytes(response);

        var decoded = decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Single(responses);
        var body = await responses[0].Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(10, body.Length);
        Assert.Equal("0123456789"u8.ToArray(), body);
    }
}
