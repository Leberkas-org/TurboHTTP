using System.Text;
using TurboHttp.Protocol;
using TurboHttp.Protocol.RFC9112;

namespace TurboHttp.Tests.RFC9112;

/// <summary>
/// Tests HTTP/1.1 decoder header size limits (security: DoS protection).
/// Verifies that oversized individual headers, total header blocks, and header counts are rejected.
/// </summary>
/// <remarks>
/// Class under test: <see cref="Http11Decoder"/>.
/// RFC 9112 §5: Header field limits prevent memory exhaustion from oversized or excessive headers.
/// </remarks>
public sealed class Http11DecoderHeaderLimitsTests
{
    private static ReadOnlyMemory<byte> Bytes(string s)
        => Encoding.ASCII.GetBytes(s);

    private static string BuildRawResponse(string statusLine, string headers, string body = "")
        => $"{statusLine}\r\n{headers}\r\n\r\n{body}";

    // ── Default limits ────────────────────────────────────────────────────────

    [Fact(DisplayName = "RFC9112-5-HL-001: Default MaxHeaderSize is 16KB")]
    public void Should_UseDefaultMaxHeaderSize_When_NoConfigProvided()
    {
        var decoder = new Http11Decoder();
        var value = new string('A', 16 * 1024 - 20); // name + ": " + value < 16KB
        var raw = BuildRawResponse("HTTP/1.1 200 OK", $"X-Big: {value}\r\nContent-Length: 0");

        var result = decoder.TryDecode(Bytes(raw), out var responses);

        Assert.True(result);
        Assert.Single(responses);
    }

    [Fact(DisplayName = "RFC9112-5-HL-002: Default MaxTotalHeaderSize is 64KB")]
    public void Should_UseDefaultMaxTotalHeaderSize_When_NoConfigProvided()
    {
        var decoder = new Http11Decoder();
        var sb = new StringBuilder();
        var headerValue = new string('B', 1000);
        for (var i = 0; i < 60; i++)
        {
            sb.Append($"X-Hdr-{i:D3}: {headerValue}\r\n");
        }
        sb.Append("Content-Length: 0");
        var raw = BuildRawResponse("HTTP/1.1 200 OK", sb.ToString());

        var result = decoder.TryDecode(Bytes(raw), out var responses);

        Assert.True(result);
        Assert.Single(responses);
    }

    [Fact(DisplayName = "RFC9112-5-HL-003: Default MaxHeaderCount is 100")]
    public void Should_UseDefaultMaxHeaderCount_When_NoConfigProvided()
    {
        var decoder = new Http11Decoder();
        // 99 extra + Content-Length = 100 total, at the limit
        var raw = BuildResponseWithNHeaders(99);

        var result = decoder.TryDecode(raw, out var responses);

        Assert.True(result);
        Assert.Single(responses);
    }

    // ── Single header too large ───────────────────────────────────────────────

    [Fact(DisplayName = "RFC9112-5-HL-004: Single header exceeding MaxHeaderSize rejected")]
    public void Should_ThrowHeaderTooLarge_When_SingleHeaderExceedsLimit()
    {
        var decoder = new Http11Decoder(maxHeaderSize: 100);
        var bigValue = new string('X', 200);
        var raw = BuildRawResponse("HTTP/1.1 200 OK", $"X-Big: {bigValue}\r\nContent-Length: 0");

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(Bytes(raw), out _));

        Assert.Equal(HttpDecoderError.HeaderTooLarge, ex.DecodeError);
        Assert.Contains("X-Big", ex.Message);
        Assert.Contains("100", ex.Message);
    }

    [Fact(DisplayName = "RFC9112-5-HL-005: Header exactly at MaxHeaderSize accepted")]
    public void Should_Accept_When_SingleHeaderExactlyAtLimit()
    {
        const int limit = 50;
        var value = new string('V', limit - 1 - 2); // "X" + ": " + value = 50
        var decoder = new Http11Decoder(maxHeaderSize: limit);
        var raw = BuildRawResponse("HTTP/1.1 200 OK", $"X: {value}\r\nContent-Length: 0");

        var result = decoder.TryDecode(Bytes(raw), out var responses);

        Assert.True(result);
        Assert.Single(responses);
    }

    [Fact(DisplayName = "RFC9112-5-HL-006: Header one byte over MaxHeaderSize rejected")]
    public void Should_ThrowHeaderTooLarge_When_OneByteOverLimit()
    {
        const int limit = 50;
        var value = new string('V', limit - 1 - 2 + 1); // one byte over
        var decoder = new Http11Decoder(maxHeaderSize: limit);
        var raw = BuildRawResponse("HTTP/1.1 200 OK", $"X: {value}\r\nContent-Length: 0");

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(Bytes(raw), out _));
        Assert.Equal(HttpDecoderError.HeaderTooLarge, ex.DecodeError);
    }

    [Fact(DisplayName = "RFC9112-5-HL-007: Multiple small headers within MaxHeaderSize accepted")]
    public void Should_Accept_When_MultipleSmallHeadersWithinLimit()
    {
        var decoder = new Http11Decoder(maxHeaderSize: 100);
        var raw = BuildRawResponse("HTTP/1.1 200 OK",
            "X-A: short\r\nX-B: also-short\r\nContent-Length: 0");

        var result = decoder.TryDecode(Bytes(raw), out var responses);

        Assert.True(result);
        Assert.Single(responses);
    }

    // ── Total headers too large ───────────────────────────────────────────────

    [Fact(DisplayName = "RFC9112-5-HL-008: Total headers exceeding MaxTotalHeaderSize rejected")]
    public void Should_ThrowTotalHeadersTooLarge_When_TotalExceedsLimit()
    {
        // Early check fires when raw header section size exceeds maxTotalHeaderSize
        var decoder = new Http11Decoder(maxHeaderSize: 1000, maxTotalHeaderSize: 200);
        var sb = new StringBuilder();
        for (var i = 0; i < 15; i++)
        {
            sb.Append($"X-Hdr-{i:D2}: value-{i:D2}\r\n");
        }
        sb.Append("Content-Length: 0");
        var raw = BuildRawResponse("HTTP/1.1 200 OK", sb.ToString());

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(Bytes(raw), out _));

        Assert.Equal(HttpDecoderError.TotalHeadersTooLarge, ex.DecodeError);
    }

    [Fact(DisplayName = "RFC9112-5-HL-009: Total headers exactly at limit accepted")]
    public void Should_Accept_When_TotalHeadersExactlyAtLimit()
    {
        // Raw header section: "HTTP/1.1 200 OK\r\nX: V\r\n" = 15+2+4+2 = 23 bytes
        // headerEnd = 23, so maxTotalHeaderSize = 23 → passes early check (23 > 23 is false)
        var decoder = new Http11Decoder(maxHeaderSize: 100, maxTotalHeaderSize: 23);
        var raw = BuildRawResponse("HTTP/1.1 200 OK", "X: V");

        var result = decoder.TryDecode(Bytes(raw), out var responses);

        Assert.True(result);
        Assert.Single(responses);
    }

    [Fact(DisplayName = "RFC9112-5-HL-010: Total headers one byte over limit rejected")]
    public void Should_ThrowTotalHeadersTooLarge_When_OneByteOverTotal()
    {
        // "X: V" = 4 bytes, "Y: WW" = 5 bytes, total = 9 > 8
        var decoder = new Http11Decoder(maxHeaderSize: 100, maxTotalHeaderSize: 8);
        var raw = BuildRawResponse("HTTP/1.1 200 OK", "X: V\r\nY: WW");

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(Bytes(raw), out _));
        Assert.Equal(HttpDecoderError.TotalHeadersTooLarge, ex.DecodeError);
    }

    // ── Header count limits ──────────────────────────────────────────────────

    [Fact(DisplayName = "RFC9112-5-HL-011: Header count exceeding MaxHeaderCount rejected")]
    public void Should_ThrowTooManyHeaders_When_CountExceedsLimit()
    {
        var decoder = new Http11Decoder(maxHeaderCount: 5);
        var raw = BuildResponseWithNHeaders(5); // 5 + Content-Length = 6 > 5

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecoderError.TooManyHeaders, ex.DecodeError);
    }

    [Fact(DisplayName = "RFC9112-5-HL-012: Header count exactly at MaxHeaderCount accepted")]
    public void Should_Accept_When_HeaderCountExactlyAtLimit()
    {
        var decoder = new Http11Decoder(maxHeaderCount: 5);
        var raw = BuildResponseWithNHeaders(4); // 4 + Content-Length = 5

        var result = decoder.TryDecode(raw, out var responses);

        Assert.True(result);
        Assert.Single(responses);
    }

    [Fact(DisplayName = "RFC9112-5-HL-013: Header count one over MaxHeaderCount rejected")]
    public void Should_ThrowTooManyHeaders_When_OneOverCountLimit()
    {
        var decoder = new Http11Decoder(maxHeaderCount: 10);
        var raw = BuildResponseWithNHeaders(10); // 10 + Content-Length = 11 > 10

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecoderError.TooManyHeaders, ex.DecodeError);
        Assert.Contains("10", ex.Message); // limit value in message
    }

    // ── Custom limits ─────────────────────────────────────────────────────────

    [Fact(DisplayName = "RFC9112-5-HL-014: Custom MaxHeaderSize respected")]
    public void Should_RejectAtCustomLimit_When_MaxHeaderSizeOverridden()
    {
        var decoder = new Http11Decoder(maxHeaderSize: 20);
        var raw = BuildRawResponse("HTTP/1.1 200 OK",
            "X-TooLong: this-value-is-way-too-long-for-limit\r\nContent-Length: 0");

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(Bytes(raw), out _));
        Assert.Equal(HttpDecoderError.HeaderTooLarge, ex.DecodeError);
    }

    [Fact(DisplayName = "RFC9112-5-HL-015: Custom MaxTotalHeaderSize respected")]
    public void Should_RejectAtCustomTotalLimit_When_MaxTotalHeaderSizeOverridden()
    {
        var decoder = new Http11Decoder(maxHeaderSize: 500, maxTotalHeaderSize: 50);
        var sb = new StringBuilder();
        for (var i = 0; i < 5; i++)
        {
            sb.Append($"X-H{i}: value-padding-{i:D4}\r\n");
        }
        sb.Append("Content-Length: 0");
        var raw = BuildRawResponse("HTTP/1.1 200 OK", sb.ToString());

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(Bytes(raw), out _));
        Assert.Equal(HttpDecoderError.TotalHeadersTooLarge, ex.DecodeError);
    }

    [Fact(DisplayName = "RFC9112-5-HL-016: Custom MaxHeaderCount respected")]
    public void Should_RejectAtCustomCountLimit_When_MaxHeaderCountOverridden()
    {
        var decoder = new Http11Decoder(maxHeaderCount: 3);
        var raw = BuildResponseWithNHeaders(3); // 3 + Content-Length = 4 > 3

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecoderError.TooManyHeaders, ex.DecodeError);
    }

    // ── Obs-fold interaction ─────────────────────────────────────────────────

    [Fact(DisplayName = "RFC9112-5-HL-017: Obs-fold continuation rejected per RFC 9112 §5.2")]
    public void Should_ThrowObsoleteFolding_When_FoldedHeaderDetected()
    {
        var decoder = new Http11Decoder(maxHeaderSize: 500);
        const string raw = "HTTP/1.1 200 OK\r\nX-Folded: part1\r\n continued-text\r\nContent-Length: 0\r\n\r\n";

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(Bytes(raw), out _));
        Assert.Equal(HttpDecoderError.ObsoleteFoldingDetected, ex.DecodeError);
    }

    [Fact(DisplayName = "RFC9112-5-HL-018: Obs-fold with tab character rejected")]
    public void Should_ThrowObsoleteFolding_When_TabFoldedHeaderDetected()
    {
        var decoder = new Http11Decoder();
        const string raw = "HTTP/1.1 200 OK\r\nX-Folded: part1\r\n\tcontinued-with-tab\r\nContent-Length: 0\r\n\r\n";

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(Bytes(raw), out _));
        Assert.Equal(HttpDecoderError.ObsoleteFoldingDetected, ex.DecodeError);
    }

    // ── Chunked body NOT subject to header limits ────────────────────────────

    [Fact(DisplayName = "RFC9112-5-HL-019: Chunked body not subject to header size limits")]
    public void Should_AcceptChunkedBody_When_BodyLargerThanHeaderLimit()
    {
        // MaxHeaderSize is tiny but chunked body is large — body must not trigger header limits
        var decoder = new Http11Decoder(maxHeaderSize: 50, maxTotalHeaderSize: 200);
        var bodyChunk = new string('D', 500);
        var chunkLen = bodyChunk.Length.ToString("X");
        var raw = $"HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n{chunkLen}\r\n{bodyChunk}\r\n0\r\n\r\n";

        var result = decoder.TryDecode(Bytes(raw), out var responses);

        Assert.True(result);
        Assert.Single(responses);
    }

    [Fact(DisplayName = "RFC9112-5-HL-020: Content-Length body not subject to header size limits")]
    public void Should_AcceptContentLengthBody_When_BodyLargerThanHeaderLimit()
    {
        var decoder = new Http11Decoder(maxHeaderSize: 50, maxTotalHeaderSize: 200);
        var body = new string('E', 500);
        var raw = BuildRawResponse("HTTP/1.1 200 OK", $"Content-Length: {body.Length}", body);

        var result = decoder.TryDecode(Bytes(raw), out var responses);

        Assert.True(result);
        Assert.Single(responses);
    }

    // ── Error messages ───────────────────────────────────────────────────────

    [Fact(DisplayName = "RFC9112-5-HL-021: Error message includes header name for single header violation")]
    public void Should_IncludeHeaderName_When_SingleHeaderTooLarge()
    {
        var decoder = new Http11Decoder(maxHeaderSize: 30);
        var raw = BuildRawResponse("HTTP/1.1 200 OK",
            "X-Offending: this-value-exceeds-the-configured-limit\r\nContent-Length: 0");

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(Bytes(raw), out _));

        Assert.Contains("X-Offending", ex.Message);
        Assert.Contains("30", ex.Message);
    }

    [Fact(DisplayName = "RFC9112-5-HL-022: Error message is descriptive for total header violation")]
    public void Should_HaveDescriptiveMessage_When_TotalHeadersTooLarge()
    {
        var decoder = new Http11Decoder(maxHeaderSize: 1000, maxTotalHeaderSize: 30);
        var raw = BuildRawResponse("HTTP/1.1 200 OK",
            "X-A: aaaaaaaaaa\r\nX-B: bbbbbbbbbb\r\nContent-Length: 0");

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(Bytes(raw), out _));

        // Error message references the security concern
        Assert.Contains("Total header size", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "RFC9112-5-HL-023: Error message includes count for header count violation")]
    public void Should_IncludeCount_When_TooManyHeaders()
    {
        var decoder = new Http11Decoder(maxHeaderCount: 3);
        var raw = BuildResponseWithNHeaders(3); // 3 + Content-Length = 4 > 3

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(raw, out _));

        Assert.Contains("3", ex.Message); // limit value
    }

    // ── Parameterless backward compat ────────────────────────────────────────

    [Fact(DisplayName = "RFC9112-5-HL-024: Parameterless construction still works")]
    public void Should_WorkWithDefaults_When_NoParametersProvided()
    {
        var decoder = new Http11Decoder();
        var raw = BuildRawResponse("HTTP/1.1 200 OK",
            "Content-Type: text/plain\r\nContent-Length: 5", "Hello");

        var result = decoder.TryDecode(Bytes(raw), out var responses);

        Assert.True(result);
        Assert.Single(responses);
    }

    // ── CONNECT response path ────────────────────────────────────────────────

    [Fact(DisplayName = "RFC9112-5-HL-025: Header limits enforced on CONNECT response path")]
    public void Should_ThrowHeaderTooLarge_When_ConnectResponseHeaderExceedsLimit()
    {
        var decoder = new Http11Decoder(maxHeaderSize: 20);
        var bigValue = new string('C', 50);
        var raw = $"HTTP/1.1 200 OK\r\nX-Connect: {bigValue}\r\n\r\n";

        var ex = Assert.Throws<HttpDecoderException>(() =>
            decoder.TryDecodeConnect(Bytes(raw), out _));
        Assert.Equal(HttpDecoderError.HeaderTooLarge, ex.DecodeError);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

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
}
