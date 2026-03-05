using System.Text;
using TurboHttp.Protocol;

namespace TurboHttp.Tests.RFC9112;

/// <summary>
/// RFC 9112 negative path hardening tests — Phase 70 Step 7.
/// Verifies the HTTP/1.1 decoder correctly rejects malformed or invalid messages,
/// covering start-line parsing, header parsing, transfer-encoding, and smuggling protection.
/// </summary>
public sealed class Http11NegativePathTests
{
    // ── RFC 9112 §4 — Start-Line Parsing ──────────────────────────────────────

    [Fact(DisplayName = "RFC9112-4-SL-001: HTTP/2.0 version in status-line rejected")]
    public void RFC9112_4_StatusLine_MustRejectHttp20Version()
    {
        // RFC 9112 §4: status-line = HTTP-version SP status-code SP reason-phrase CRLF
        // HTTP-version must be "HTTP/1.1" or "HTTP/1.0"; "HTTP/2.0" is not a valid HTTP/1.1 status line.
        var decoder = new Http11Decoder();
        var raw = "HTTP/2.0 200 OK\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecodeError.InvalidStatusLine, ex.DecodeError);
    }

    [Fact(DisplayName = "RFC9112-4-SL-002: Non-HTTP protocol prefix in status-line rejected")]
    public void RFC9112_4_StatusLine_MustRejectNonHttpProtocol()
    {
        // "HTTPS/1.1" is not a valid HTTP-version token.
        var decoder = new Http11Decoder();
        var raw = "HTTPS/1.1 200 OK\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecodeError.InvalidStatusLine, ex.DecodeError);
    }

    [Fact(DisplayName = "RFC9112-4-SL-003: Double space between HTTP-version and status code rejected")]
    public void RFC9112_4_StatusLine_MustRejectDoubleSpaceBeforeStatusCode()
    {
        // RFC 9112 §4: exactly one SP between HTTP-version and 3-digit status code.
        // "HTTP/1.1  200 OK" has a leading space before the status digits, making it unparseable.
        var decoder = new Http11Decoder();
        var raw = "HTTP/1.1  200 OK\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecodeError.InvalidStatusLine, ex.DecodeError);
    }

    [Fact(DisplayName = "RFC9112-4-SL-004: Two-digit status code rejected")]
    public void RFC9112_4_StatusLine_MustRejectTwoDigitStatusCode()
    {
        // RFC 9112 §4: status-code is exactly 3 decimal digits.
        var decoder = new Http11Decoder();
        var raw = "HTTP/1.1 20 OK\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecodeError.InvalidStatusLine, ex.DecodeError);
    }

    [Fact(DisplayName = "RFC9112-4-SL-005: Non-digit character in status code rejected")]
    public void RFC9112_4_StatusLine_MustRejectNonDigitInStatusCode()
    {
        // Status code must be exactly 3 ASCII digits.
        var decoder = new Http11Decoder();
        var raw = "HTTP/1.1 20A OK\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecodeError.InvalidStatusLine, ex.DecodeError);
    }

    [Fact(DisplayName = "RFC9112-4-SL-006: Bare LF (no CR) line endings are not recognized as CRLF")]
    public void RFC9112_4_StatusLine_BareLineFeedNeverDecodes()
    {
        // RFC 9112 §2.2: a recipient MUST NOT treat a bare LF as a line terminator.
        // Our decoder uses strict CRLF matching; bare-LF input is treated as incomplete data.
        var decoder = new Http11Decoder();
        var raw = "HTTP/1.1 200 OK\nContent-Length: 0\n\n"u8.ToArray();

        var decoded = decoder.TryDecode(raw, out var responses);

        Assert.False(decoded, "Bare-LF response should not be decoded (no valid CRLF terminator found).");
        Assert.Empty(responses);
    }

    [Fact(DisplayName = "RFC9112-4-SL-007: Overlong reason phrase hits header section size limit")]
    public void RFC9112_4_StatusLine_OverlongReasonPhraseCaughtByHeaderLimit()
    {
        // The 8 KB header section size guard also protects against overlong status lines.
        // A reason phrase that makes the entire header block exceed 8 KB is rejected.
        var decoder = new Http11Decoder(); // default 8192-byte header limit
        var longReason = new string('X', 9000);
        var raw = Encoding.ASCII.GetBytes($"HTTP/1.1 200 {longReason}\r\nContent-Length: 0\r\n\r\n");

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecodeError.LineTooLong, ex.DecodeError);
    }

    // ── RFC 9112 §5 — Header Field Parsing ────────────────────────────────────

    [Fact(DisplayName = "RFC9112-5-HDR-001: Chunked trailer without colon rejected")]
    public void RFC9112_5_Header_ChunkedTrailerWithoutColonRejected()
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
        Assert.Equal(HttpDecodeError.InvalidHeader, ex.DecodeError);
    }

    [Fact(DisplayName = "RFC9112-5-HDR-002: Chunked trailer with empty field name rejected")]
    public void RFC9112_5_Header_ChunkedTrailerEmptyFieldNameRejected()
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
        Assert.Equal(HttpDecodeError.InvalidHeader, ex.DecodeError);
    }

    // ── RFC 9112 §6 — Transfer-Encoding & Body Framing ────────────────────────

    [Fact(DisplayName =
        "RFC9112-6-TE-001: Transfer-Encoding: gzip (non-chunked) with no Content-Length yields empty body")]
    public void RFC9112_6_TransferEncoding_NonChunkedWithoutContentLength_YieldsEmptyBody()
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

    [Fact(DisplayName = "RFC9112-6-TE-002: Bytes after Content-Length boundary are not consumed by current response")]
    public async Task RFC9112_6_Body_BytesAfterContentLengthTreatedAsPipelinedResponse()
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

        var body1 = await responses[0].Content.ReadAsByteArrayAsync();
        var body2 = await responses[1].Content.ReadAsByteArrayAsync();

        Assert.Equal("Hello"u8.ToArray(), body1);
        Assert.Equal("Bye"u8.ToArray(), body2);
    }

    // ── RFC 9110 §15 — No-Body Status Codes ───────────────────────────────────

    [Fact(DisplayName =
        "RFC9110-15-204-001: 204 No Content always produces empty body regardless of Content-Length header")]
    public async Task RFC9110_15_Response204_AlwaysHasEmptyBody()
    {
        // RFC 9110 §15.3.5: A 204 response MUST NOT include a message body.
        // The decoder must return an empty body even if Content-Length is present in headers.
        var decoder = new Http11Decoder();
        var raw = "HTTP/1.1 204 No Content\r\nContent-Length: 10\r\n\r\n"u8.ToArray();

        var decoded = decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Single(responses);
        Assert.Equal(System.Net.HttpStatusCode.NoContent, responses[0].StatusCode);

        var body = await responses[0].Content.ReadAsByteArrayAsync();
        Assert.Empty(body);
    }

    [Fact(DisplayName =
        "RFC9110-15-304-001: 304 Not Modified always produces empty body regardless of Content-Length header")]
    public async Task RFC9110_15_Response304_AlwaysHasEmptyBody()
    {
        // RFC 9110 §15.4.5: A 304 response MUST NOT contain a message body.
        var decoder = new Http11Decoder();
        var raw = "HTTP/1.1 304 Not Modified\r\nContent-Length: 20\r\nETag: \"abc\"\r\n\r\n"u8.ToArray();

        var decoded = decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Single(responses);
        Assert.Equal(System.Net.HttpStatusCode.NotModified, responses[0].StatusCode);

        var body = await responses[0].Content.ReadAsByteArrayAsync();
        Assert.Empty(body);
    }

    // ── RFC 9112 §9 — Request/Response Smuggling Protection ───────────────────

    [Fact(DisplayName = "RFC9112-9-SMUG-001: Multiple Content-Length with same value is accepted per RFC 9112 §6.3")]
    public async Task RFC9112_9_MultipleContentLength_SameValue_Accepted()
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
        var body = await responses[0].Content.ReadAsByteArrayAsync();
        Assert.Equal("Hello"u8.ToArray(), body);
    }

    [Fact(DisplayName = "RFC9112-9-SMUG-002: Multiple Content-Length with differing values is a parse error")]
    public void RFC9112_9_MultipleContentLength_DifferentValues_Rejected()
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
        Assert.Equal(HttpDecodeError.MultipleContentLengthValues, ex.DecodeError);
    }

    [Fact(DisplayName = "RFC9112-9-SMUG-003: Transfer-Encoding + Content-Length combination rejected")]
    public void RFC9112_9_TransferEncodingAndContentLength_Rejected()
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
        Assert.Equal(HttpDecodeError.ChunkedWithContentLength, ex.DecodeError);
    }

    // ── RFC 9112 §7.1 — Chunked Transfer-Encoding ─────────────────────────────

    [Fact(DisplayName = "RFC9112-7-CHK-001: Chunk size of zero with extra data in line rejected as parse error")]
    public void RFC9112_7_Chunked_ZeroSizeLineWithNonNumericCharactersRejected()
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
        Assert.Equal(HttpDecodeError.InvalidChunkSize, ex.DecodeError);
    }

    [Fact(DisplayName = "RFC9112-7-CHK-002: Chunked body with all-caps hex chunk size accepted")]
    public async Task RFC9112_7_Chunked_UpperCaseHexChunkSizeAccepted()
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
        var body = await responses[0].Content.ReadAsByteArrayAsync();
        Assert.Equal(10, body.Length);
        Assert.Equal("0123456789"u8.ToArray(), body);
    }
}