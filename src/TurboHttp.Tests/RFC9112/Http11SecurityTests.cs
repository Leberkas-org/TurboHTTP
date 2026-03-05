using System.Text;
using TurboHttp.Protocol;

namespace TurboHttp.Tests.RFC9112;

public sealed class Http11SecurityTests
{
    // ── HTTP/1.1 Header Count Limits ──────────────────────────────────────────

    [Fact(DisplayName = "SEC-001a: 100 headers accepted at default limit")]
    public void Should_Accept100Headers_When_AtDefaultLimit()
    {
        var decoder = new Http11Decoder(); // default maxHeaderCount = 100
        var raw = BuildResponseWithNHeaders(99); // 99 + Content-Length = 100
        var decoded = decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Single(responses);
    }

    [Fact(DisplayName = "SEC-001b: 101 headers rejected above default limit")]
    public void Should_Reject101Headers_When_AboveDefaultLimit()
    {
        var decoder = new Http11Decoder();
        var raw = BuildResponseWithNHeaders(100); // 100 + Content-Length = 101

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecodeError.TooManyHeaders, ex.DecodeError);
    }

    [Fact(DisplayName = "SEC-001c: Custom header count limit respected")]
    public void Should_RejectAtCustomLimit_When_HeaderCountExceeded()
    {
        var decoder = new Http11Decoder(maxHeaderCount: 5);
        var raw = BuildResponseWithNHeaders(5); // 5 + Content-Length = 6

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecodeError.TooManyHeaders, ex.DecodeError);
    }

    // ── HTTP/1.1 Header Block Size Limits ─────────────────────────────────────

    [Fact(DisplayName = "SEC-002a: Header block below 8KB limit accepted")]
    public void Should_AcceptHeaderBlock_When_Below8KBLimit()
    {
        var decoder = new Http11Decoder();
        // 8191 bytes before the CRLFCRLF terminator
        var raw = BuildResponseWithHeaderBlockPosition(8191);
        var decoded = decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Single(responses);
    }

    [Fact(DisplayName = "SEC-002b: Header block above 8KB limit rejected")]
    public void Should_RejectHeaderBlock_When_Above8KBLimit()
    {
        var decoder = new Http11Decoder();
        // 8193 bytes before the CRLFCRLF terminator
        var raw = BuildResponseWithHeaderBlockPosition(8193);

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecodeError.LineTooLong, ex.DecodeError);
    }

    [Fact(DisplayName = "SEC-002c: Single header value exceeding limit rejected")]
    public void Should_RejectSingleHeader_When_ValueExceedsLimit()
    {
        var decoder = new Http11Decoder();
        var raw = BuildResponseWithLargeHeaderValue(9000);

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecodeError.LineTooLong, ex.DecodeError);
    }

    // ── HTTP/1.1 Body Size Limits ─────────────────────────────────────────────

    [Fact(DisplayName = "SEC-003a: Body at configurable limit accepted")]
    public void Should_AcceptBody_When_AtConfigurableLimit()
    {
        var decoder = new Http11Decoder(maxBodySize: 1024);
        var raw = BuildResponseWithBodySize(1024);
        var decoded = decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Single(responses);
    }

    [Fact(DisplayName = "SEC-003b: Body exceeding limit rejected")]
    public void Should_RejectBody_When_ExceedingLimit()
    {
        var decoder = new Http11Decoder(maxBodySize: 1024);
        var raw = BuildResponseWithContentLengthOnly(1025);

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecodeError.InvalidContentLength, ex.DecodeError);
    }

    [Fact(DisplayName = "SEC-003c: Zero body limit rejects any body")]
    public void Should_RejectBody_When_ZeroBodyLimit()
    {
        var decoder = new Http11Decoder(maxBodySize: 0);
        var raw = BuildResponseWithContentLengthOnly(1);

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecodeError.InvalidContentLength, ex.DecodeError);
    }

    // ── HTTP Smuggling ────────────────────────────────────────────────────────

    [Fact(DisplayName = "SEC-005a: Transfer-Encoding + Content-Length rejected")]
    public void Should_RejectResponse_When_BothTransferEncodingAndContentLengthPresent()
    {
        var decoder = new Http11Decoder();
        var raw = BuildResponseWithTeAndCl();

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecodeError.ChunkedWithContentLength, ex.DecodeError);
    }

    [Fact(DisplayName = "SEC-005b: CRLF injection in header value rejected")]
    public void Should_RejectHeader_When_CrlfInjectedInValue()
    {
        var decoder = new Http11Decoder();
        var raw = BuildResponseWithBareCrInHeaderValue();

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecodeError.InvalidFieldValue, ex.DecodeError);
    }

    [Fact(DisplayName = "SEC-005c: NUL byte in decoded header value rejected")]
    public void Should_RejectHeader_When_NulByteInValue()
    {
        var decoder = new Http11Decoder();
        var raw = BuildResponseWithNulInHeaderValue();

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecodeError.InvalidFieldValue, ex.DecodeError);
    }

    // ── State Isolation ───────────────────────────────────────────────────────

    [Fact(DisplayName = "SEC-006a: Reset() after partial headers restores clean state")]
    public void Should_DecodeCleanly_When_ResetAfterPartialHeaders()
    {
        var decoder = new Http11Decoder();

        // Feed incomplete headers (no CRLFCRLF yet)
        var incomplete = "HTTP/1.1 200 OK\r\nContent-Length: 5\r\n"u8.ToArray();
        var gotResponse = decoder.TryDecode(incomplete, out _);
        Assert.False(gotResponse);

        // Reset clears remainder
        decoder.Reset();

        // Feed a complete valid response — decoder must behave as if fresh
        var complete = "HTTP/1.1 200 OK\r\nContent-Length: 5\r\n\r\nHello"u8.ToArray();
        var decoded = decoder.TryDecode(complete, out var responses);

        Assert.True(decoded);
        Assert.Single(responses);
    }

    [Fact(DisplayName = "SEC-006b: Reset() after partial body restores clean state")]
    public void Should_DecodeCleanly_When_ResetAfterPartialBody()
    {
        var decoder = new Http11Decoder();

        // Feed headers + partial body (body says 10 bytes but we only send 5)
        var partial = "HTTP/1.1 200 OK\r\nContent-Length: 10\r\n\r\nHello"u8.ToArray();
        var gotResponse = decoder.TryDecode(partial, out _);
        Assert.False(gotResponse);

        // Reset discards the partial state
        decoder.Reset();

        // Feed a complete valid response
        var complete = "HTTP/1.1 200 OK\r\nContent-Length: 5\r\n\r\nWorld"u8.ToArray();
        var decoded = decoder.TryDecode(complete, out var responses);

        Assert.True(decoded);
        Assert.Single(responses);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Build a response with <paramref name="extraCount"/> extra X-Header-N headers
    /// plus one Content-Length header. Total header fields = extraCount + 1.
    /// </summary>
    private static ReadOnlyMemory<byte> BuildResponseWithNHeaders(int extraCount)
    {
        var sb = new StringBuilder();
        sb.Append("HTTP/1.1 200 OK\r\n");
        sb.Append("Content-Length: 0\r\n");
        for (var i = 0; i < extraCount; i++)
        {
            sb.Append($"X-Header-{i:D3}: value\r\n");
        }

        sb.Append("\r\n");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    /// <summary>
    /// Build a response whose header section occupies exactly <paramref name="headerEnd"/> bytes
    /// before the CRLFCRLF terminator (i.e., FindCrlfCrlf returns headerEnd).
    /// Structure: "HTTP/1.1 200 OK\r\n" (17 B) + "X-Padding: " (11 B) + padding + "\r\n" (2 B)
    /// headerEnd = 17 + 11 + paddingLength = 28 + paddingLength → paddingLength = headerEnd - 28.
    /// </summary>
    private static ReadOnlyMemory<byte> BuildResponseWithHeaderBlockPosition(int headerEnd)
    {
        var paddingLength = headerEnd - 28;
        var padding = new string('a', paddingLength);
        var raw = $"HTTP/1.1 200 OK\r\nX-Padding: {padding}\r\n\r\n";
        return Encoding.ASCII.GetBytes(raw);
    }

    /// <summary>
    /// Build a response with a single header value of <paramref name="valueLength"/> bytes,
    /// which causes the header block to exceed the 8 KB limit.
    /// </summary>
    private static ReadOnlyMemory<byte> BuildResponseWithLargeHeaderValue(int valueLength)
    {
        var value = new string('x', valueLength);
        var raw = $"HTTP/1.1 200 OK\r\nX-Big: {value}\r\n\r\n";
        return Encoding.ASCII.GetBytes(raw);
    }

    /// <summary>
    /// Build a fully valid response with exactly <paramref name="bodySize"/> bytes of body.
    /// </summary>
    private static ReadOnlyMemory<byte> BuildResponseWithBodySize(int bodySize)
    {
        var body = new string('B', bodySize);
        var raw = $"HTTP/1.1 200 OK\r\nContent-Length: {bodySize}\r\n\r\n{body}";
        return Encoding.ASCII.GetBytes(raw);
    }

    /// <summary>
    /// Build a response that declares Content-Length = <paramref name="contentLength"/>
    /// but contains no body bytes. Used to test that the limit check fires on the
    /// Content-Length value alone (before reading body bytes).
    /// </summary>
    private static ReadOnlyMemory<byte> BuildResponseWithContentLengthOnly(int contentLength)
    {
        var raw = $"HTTP/1.1 200 OK\r\nContent-Length: {contentLength}\r\n\r\n";
        return Encoding.ASCII.GetBytes(raw);
    }

    /// <summary>
    /// Build a response that has both Transfer-Encoding: chunked and Content-Length headers.
    /// </summary>
    private static ReadOnlyMemory<byte> BuildResponseWithTeAndCl()
    {
        const string response =
            "HTTP/1.1 200 OK\r\n" +
            "Transfer-Encoding: chunked\r\n" +
            "Content-Length: 5\r\n" +
            "\r\n" +
            "5\r\nHello\r\n0\r\n\r\n";
        return Encoding.ASCII.GetBytes(response);
    }

    /// <summary>
    /// Build a response where a header value contains a bare CR (0x0D) character.
    /// The HTTP/1.1 parser extracts the value as a string containing '\r',
    /// which is invalid per RFC 9112 §5.5.
    /// </summary>
    private static ReadOnlyMemory<byte> BuildResponseWithBareCrInHeaderValue()
    {
        // Manually build bytes to embed a bare \r inside a header value.
        // Bytes: "HTTP/1.1 200 OK\r\n" + "X-Foo: hello\rworld\r\n" + "Content-Length: 0\r\n" + "\r\n"
        var prefix = "HTTP/1.1 200 OK\r\nX-Foo: hello"u8.ToArray();
        var bareCr = new byte[] { 0x0D }; // bare CR (not followed by LF)
        var suffix = "world\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var bytes = new byte[prefix.Length + bareCr.Length + suffix.Length];
        prefix.CopyTo(bytes, 0);
        bareCr.CopyTo(bytes, prefix.Length);
        suffix.CopyTo(bytes, prefix.Length + bareCr.Length);
        return bytes;
    }

    /// <summary>
    /// Build a response where a header value contains a NUL (0x00) byte.
    /// </summary>
    private static ReadOnlyMemory<byte> BuildResponseWithNulInHeaderValue()
    {
        var prefix = "HTTP/1.1 200 OK\r\nX-Foo: hello"u8.ToArray();
        var nul = new byte[] { 0x00 };
        var suffix = "world\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var bytes = new byte[prefix.Length + nul.Length + suffix.Length];
        prefix.CopyTo(bytes, 0);
        nul.CopyTo(bytes, prefix.Length);
        suffix.CopyTo(bytes, prefix.Length + nul.Length);
        return bytes;
    }
}
