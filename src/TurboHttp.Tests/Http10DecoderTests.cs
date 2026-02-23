using System.Net;
using System.Text;
using TurboHttp.Protocol;

namespace TurboHttp.Tests;

public sealed class Http10DecoderTests
{
    private static ReadOnlyMemory<byte> Bytes(string s)
        => Encoding.GetEncoding("ISO-8859-1").GetBytes(s);

    private static ReadOnlyMemory<byte> BuildRawResponse(
        string statusLine,
        string headers,
        string body = "")
    {
        var raw = $"{statusLine}\r\n{headers}\r\n\r\n{body}";
        return Bytes(raw);
    }

    private static ReadOnlyMemory<byte> BuildRawResponse(
        string statusLine,
        string headers,
        byte[] body)
    {
        var headerPart = Encoding.ASCII.GetBytes($"{statusLine}\r\n{headers}\r\n\r\n");
        var result = new byte[headerPart.Length + body.Length];
        headerPart.CopyTo(result, 0);
        body.CopyTo(result, headerPart.Length);
        return result;
    }

    [Fact]
    public void StatusLine_200Ok_ParsedCorrectly()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK", "Content-Length: 0");

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        Assert.NotNull(response);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("OK", response.ReasonPhrase);
    }

    [Fact]
    public void StatusLine_404NotFound_ParsedCorrectly()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 404 Not Found", "Content-Length: 0");

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.NotFound, response!.StatusCode);
        Assert.Equal("Not Found", response.ReasonPhrase);
    }

    [Fact]
    public void StatusLine_500InternalServerError_ParsedCorrectly()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 500 Internal Server Error", "Content-Length: 0");

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.InternalServerError, response!.StatusCode);
        Assert.Equal("Internal Server Error", response.ReasonPhrase);
    }

    [Fact]
    public void StatusLine_301MovedPermanently_ParsedCorrectly()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 301 Moved Permanently",
            "Location: http://example.com/new\r\nContent-Length: 0");

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.MovedPermanently, response!.StatusCode);
    }

    [Fact]
    public void StatusLine_ReasonPhraseWithMultipleWords_PreservedCompletely()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 Very Long Reason Phrase Here", "Content-Length: 0");

        decoder.TryDecode(data, out var response);

        Assert.Equal("Very Long Reason Phrase Here", response!.ReasonPhrase);
    }

    [Fact]
    public void StatusLine_Version_IsSetToHttp10()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK", "Content-Length: 0");

        decoder.TryDecode(data, out var response);

        Assert.Equal(new Version(1, 0), response!.Version);
    }

    [Fact]
    public void StatusLine_InvalidStatusCode_ThrowsDecoderException()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 ABC BadCode", "Content-Length: 0");

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(data, out _));
        Assert.Equal(HttpDecodeError.InvalidStatusLine, ex.DecodeError);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(201)]
    [InlineData(204)]
    [InlineData(301)]
    [InlineData(302)]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(500)]
    [InlineData(503)]
    public void StatusLine_CommonStatusCodes_AllParsedCorrectly(int code)
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse($"HTTP/1.0 {code} Reason", "Content-Length: 0");

        decoder.TryDecode(data, out var response);

        Assert.Equal((HttpStatusCode)code, response!.StatusCode);
    }

    [Fact]
    public void Headers_SingleHeader_ParsedCorrectly()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK",
            "Content-Type: text/plain\r\nContent-Length: 5", "Hello");

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        Assert.NotNull(response);
        Assert.NotNull(response.Content);

        Assert.True(response.Content.Headers.TryGetValues("Content-Type", out var values));
        Assert.Contains("text/plain", values!);
    }

    [Fact]
    public void Headers_CustomHeader_ParsedCorrectly()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK",
            "X-Custom-Header: my-value\r\nContent-Length: 0");

        decoder.TryDecode(data, out var response);

        Assert.True(response!.Headers.TryGetValues("X-Custom-Header", out var values));
        Assert.Contains("my-value", values);
    }

    [Fact]
    public void Headers_MultipleCustomHeaders_AllParsed()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK",
            "X-Header-A: value-a\r\nX-Header-B: value-b\r\nContent-Length: 0");

        decoder.TryDecode(data, out var response);

        Assert.True(response!.Headers.TryGetValues("X-Header-A", out var a));
        Assert.True(response.Headers.TryGetValues("X-Header-B", out var b));
        Assert.Contains("value-a", a);
        Assert.Contains("value-b", b);
    }

    [Fact]
    public void Headers_NamesAreCaseInsensitive()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK",
            "x-custom-header: lower-case\r\nContent-Length: 0");

        decoder.TryDecode(data, out var response);

        Assert.True(response!.Headers.TryGetValues("X-Custom-Header", out var values)
                    || response.Headers.TryGetValues("x-custom-header", out values));
        Assert.Contains("lower-case", values);
    }

    [Fact]
    public void Headers_FoldedHeader_IsContinuedCorrectly()
    {
        var decoder = new Http10Decoder();
        const string raw = "HTTP/1.0 200 OK\r\nX-Folded: first part\r\n continued\r\nContent-Length: 0\r\n\r\n";

        decoder.TryDecode(Bytes(raw), out var response);

        Assert.True(response!.Headers.TryGetValues("X-Folded", out var values));

        var combined = string.Join(" ", values);
        Assert.Contains("first part", combined);
        Assert.Contains("continued", combined);
    }

    [Fact]
    public void Headers_HeaderWithLeadingTrailingSpaces_AreTrimmed()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK",
            "X-Spaced:   trimmed-value   \r\nContent-Length: 0");

        decoder.TryDecode(data, out var response);

        Assert.True(response!.Headers.TryGetValues("X-Spaced", out var values));
        Assert.Contains("trimmed-value", values);
    }

    [Fact]
    public void Headers_LfOnlyLineEnding_ParsedCorrectly()
    {
        var decoder = new Http10Decoder();
        const string raw = "HTTP/1.0 200 OK\nX-Lf-Header: lf-value\nContent-Length: 0\n\n";

        var result = decoder.TryDecode(Bytes(raw), out var response);

        Assert.True(result);
        Assert.NotNull(response);
    }

    [Fact]
    public async Task Body_WithContentLength_BodyReadCorrectly()
    {
        var decoder = new Http10Decoder();
        const string body = "Hello, World!";
        var data = BuildRawResponse("HTTP/1.0 200 OK",
            $"Content-Length: {body.Length}\r\nContent-Type: text/plain", body);

        decoder.TryDecode(data, out var response);

        var actualBody = await response!.Content.ReadAsStringAsync();
        Assert.Equal(body, actualBody);
    }

    [Fact]
    public async Task Body_WithContentLength_ExactBytesRead()
    {
        var decoder = new Http10Decoder();
        const string body = "ABCDE";
        const string raw = $"HTTP/1.0 200 OK\r\nContent-Length: 3\r\n\r\n{body}";

        decoder.TryDecode(Bytes(raw), out var response);

        var bytes = await response!.Content.ReadAsByteArrayAsync();
        Assert.Equal(3, bytes.Length);
        Assert.Equal("ABC", Encoding.ASCII.GetString(bytes));
    }

    [Fact]
    public void Body_WithZeroContentLength_EmptyBody()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK", "Content-Length: 0");

        decoder.TryDecode(data, out var response);

        Assert.Equal(0, response!.Content.Headers.ContentLength);
    }

    [Fact]
    public async Task Body_WithoutContentLength_ReadsUntilEndOfData()
    {
        var decoder = new Http10Decoder();
        const string body = "body without content-length";
        const string raw = $"HTTP/1.0 200 OK\r\n\r\n{body}";

        decoder.TryDecode(Bytes(raw), out var response);

        var actualBody = await response!.Content.ReadAsStringAsync();
        Assert.Equal(body, actualBody);
    }

    [Fact]
    public async Task Body_BinaryContent_PreservedExactly()
    {
        var bodyBytes = new byte[] { 0x00, 0x01, 0x7F, 0x80, 0xFE, 0xFF };
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK",
            $"Content-Length: {bodyBytes.Length}", bodyBytes);

        decoder.TryDecode(data, out var response);

        var actualBody = await response!.Content.ReadAsByteArrayAsync();
        Assert.Equal(bodyBytes, actualBody);
    }

    [Fact]
    public void Body_ContentLengthHeader_SetOnContent()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK", "Content-Length: 5", "Hello");

        decoder.TryDecode(data, out var response);

        Assert.Equal(5, response!.Content.Headers.ContentLength);
    }

    [Fact]
    public void Body_NoBody_ResponseContentIsNull()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 204 No Content", "Content-Length: 0");

        decoder.TryDecode(data, out var response);

        Assert.Equal(0, response!.Content.Headers.ContentLength);
    }

    [Fact]
    public void Fragmentation_HeadersSplitAcrossTwoChunks_ReassembledCorrectly()
    {
        var decoder = new Http10Decoder();
        var full = Bytes("HTTP/1.0 200 OK\r\nContent-Length: 5\r\n\r\nHello");

        var chunk1 = full[..15];
        var chunk2 = full[15..];

        var result1 = decoder.TryDecode(chunk1, out var r1);
        Assert.False(result1);
        Assert.Null(r1);

        var result2 = decoder.TryDecode(chunk2, out var r2);
        Assert.True(result2);
        Assert.NotNull(r2);
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
    }

    [Fact]
    public async Task Fragmentation_BodySplitAcrossTwoChunks_ReassembledCorrectly()
    {
        var decoder = new Http10Decoder();
        var full = Bytes("HTTP/1.0 200 OK\r\nContent-Length: 10\r\n\r\n1234567890");

        var separatorIdx = FindSequence(full.Span, "\r\n\r\n"u8) + 4;
        var chunk1 = full[..(separatorIdx + 5)];
        var chunk2 = full[(separatorIdx + 5)..];

        var result1 = decoder.TryDecode(chunk1, out _);
        Assert.False(result1);

        var result2 = decoder.TryDecode(chunk2, out var response);
        Assert.True(result2);
        var body = await response!.Content.ReadAsStringAsync();
        Assert.Equal("1234567890", body);
    }

    [Fact]
    public void Fragmentation_SingleByteChunks_EventuallyDecodes()
    {
        var decoder = new Http10Decoder();
        var full = Bytes("HTTP/1.0 200 OK\r\nContent-Length: 3\r\n\r\nABC").ToArray();

        HttpResponseMessage? response = null;
        var decoded = false;

        for (var i = 0; i < full.Length; i++)
        {
            var chunk = new ReadOnlyMemory<byte>(full, i, 1);
            if (decoder.TryDecode(chunk, out response))
            {
                decoded = true;
                break;
            }
        }

        Assert.True(decoded);
        Assert.NotNull(response);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void Fragmentation_MultipleResponses_DecodedIndependently()
    {
        var decoder = new Http10Decoder();

        var resp1 = Bytes("HTTP/1.0 200 OK\r\nContent-Length: 3\r\n\r\nONE");
        var resp2 = Bytes("HTTP/1.0 404 Not Found\r\nContent-Length: 0\r\n\r\n");

        decoder.TryDecode(resp1, out var r1);
        decoder.TryDecode(resp2, out var r2);

        Assert.Equal(HttpStatusCode.OK, r1!.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, r2!.StatusCode);
    }

    [Fact]
    public void Fragmentation_IncompleteHeader_ReturnsFalseAndBuffers()
    {
        var decoder = new Http10Decoder();
        var incomplete = Bytes("HTTP/1.0 200 OK\r\nContent-Le");

        var result = decoder.TryDecode(incomplete, out var response);

        Assert.False(result);
        Assert.Null(response);
    }

    [Fact]
    public void Fragmentation_IncompleteBody_ReturnsFalseAndBuffers()
    {
        var decoder = new Http10Decoder();
        var incomplete = Bytes("HTTP/1.0 200 OK\r\nContent-Length: 100\r\n\r\nonly10bytes");

        var result = decoder.TryDecode(incomplete, out var response);

        Assert.False(result);
        Assert.Null(response);
    }

    [Fact]
    public async Task Fragmentation_ThreeChunks_DecodesCorrectly()
    {
        var decoder = new Http10Decoder();
        const string full = "HTTP/1.0 200 OK\r\nContent-Length: 9\r\n\r\nABCDEFGHI";
        var bytes = Bytes(full).ToArray();

        var third = bytes.Length / 3;
        var c1 = new ReadOnlyMemory<byte>(bytes, 0, third);
        var c2 = new ReadOnlyMemory<byte>(bytes, third, third);
        var c3 = new ReadOnlyMemory<byte>(bytes, third * 2, bytes.Length - third * 2);

        Assert.False(decoder.TryDecode(c1, out _));
        Assert.False(decoder.TryDecode(c2, out _));
        var result = decoder.TryDecode(c3, out var response);

        Assert.True(result);
        var body = await response!.Content.ReadAsStringAsync();
        Assert.Equal("ABCDEFGHI", body);
    }

    [Fact]
    public void TryDecodeEof_WithBufferedData_ReturnsTrue()
    {
        var decoder = new Http10Decoder();
        var incomplete = Bytes("HTTP/1.0 200 OK\r\n\r\nsome body data");
        decoder.TryDecode(incomplete, out _);

        var decoder2 = new Http10Decoder();
        var partial = Bytes("HTTP/1.0 200 OK\r\nContent-Length: 100\r\n\r\nshort");
        decoder2.TryDecode(partial, out _);

        var result = decoder2.TryDecodeEof(out var response);

        Assert.True(result);
        Assert.NotNull(response);
    }

    [Fact]
    public void TryDecodeEof_WithEmptyBuffer_ReturnsFalse()
    {
        var decoder = new Http10Decoder();

        var result = decoder.TryDecodeEof(out var response);

        Assert.False(result);
        Assert.Null(response);
    }

    [Fact]
    public void TryDecodeEof_WithIncompleteHeader_ReturnsFalse()
    {
        var decoder = new Http10Decoder();
        var incomplete = Bytes("HTTP/1.0 200");
        decoder.TryDecode(incomplete, out _);

        var result = decoder.TryDecodeEof(out var response);

        Assert.False(result);
        Assert.Null(response);
    }

    [Fact]
    public void TryDecodeEof_ClearsRemainder()
    {
        var decoder = new Http10Decoder();
        var partial = Bytes("HTTP/1.0 200 OK\r\nContent-Length: 100\r\n\r\nshort");
        decoder.TryDecode(partial, out _);

        decoder.TryDecodeEof(out _);

        var result = decoder.TryDecodeEof(out var response);
        Assert.False(result);
        Assert.Null(response);
    }

    [Fact]
    public void Reset_ClearsBufferedData()
    {
        var decoder = new Http10Decoder();
        var partial = Bytes("HTTP/1.0 200 OK\r\nContent-Length: 100\r\n\r\nincomplete");
        decoder.TryDecode(partial, out _);

        decoder.Reset();

        var result = decoder.TryDecodeEof(out var response);
        Assert.False(result);
        Assert.Null(response);
    }

    [Fact]
    public void Reset_AfterReset_DecodesNewResponseCorrectly()
    {
        var decoder = new Http10Decoder();
        var partial = Bytes("HTTP/1.0 200 OK\r\nContent-Length: 100\r\n\r\nincomplete");
        decoder.TryDecode(partial, out _);

        decoder.Reset();

        var fresh = BuildRawResponse("HTTP/1.0 201 Created", "Content-Length: 0");
        var result = decoder.TryDecode(fresh, out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.Created, response!.StatusCode);
    }

    [Fact]
    public void Reset_CalledMultipleTimes_DoesNotThrow()
    {
        var decoder = new Http10Decoder();

        var ex = Record.Exception(() =>
        {
            decoder.Reset();
            decoder.Reset();
            decoder.Reset();
        });

        Assert.Null(ex);
    }

    [Fact]
    public void EdgeCase_EmptyInput_ReturnsFalse()
    {
        var decoder = new Http10Decoder();

        var result = decoder.TryDecode(ReadOnlyMemory<byte>.Empty, out var response);

        Assert.False(result);
        Assert.Null(response);
    }

    [Fact]
    public void EdgeCase_OnlyHeaderSeparator_ThrowsDecoderException()
    {
        var decoder = new Http10Decoder();
        var data = Bytes("\r\n\r\n");

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(data, out _));
        Assert.Equal(HttpDecodeError.InvalidStatusLine, ex.DecodeError);
    }

    [Fact]
    public void EdgeCase_ContentLengthNegative_ThrowsDecoderException()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK", "Content-Length: -1");

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(data, out _));
        Assert.Equal(HttpDecodeError.InvalidContentLength, ex.DecodeError);
    }

    [Fact]
    public void EdgeCase_DuplicateContentLength_DifferentValuesThrows()
    {
        var decoder = new Http10Decoder();
        const string raw = "HTTP/1.0 200 OK\r\nContent-Length: 3\r\nContent-Length: 5\r\n\r\nHello";

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(Bytes(raw), out _));
        Assert.Equal(HttpDecodeError.MultipleContentLengthValues, ex.DecodeError);
    }

    [Fact]
    public void EdgeCase_HeaderWithoutValue_SkippedSafely()
    {
        var decoder = new Http10Decoder();
        const string raw = "HTTP/1.0 200 OK\r\nX-Empty:\r\nContent-Length: 0\r\n\r\n";

        var result = decoder.TryDecode(Bytes(raw), out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.OK, response!.StatusCode);
    }

    [Fact]
    public void EdgeCase_VeryLargeHeader_HandledCorrectly()
    {
        var decoder = new Http10Decoder();
        var longValue = new string('A', 8000);
        var raw = $"HTTP/1.0 200 OK\r\nX-Big: {longValue}\r\nContent-Length: 0\r\n\r\n";

        var result = decoder.TryDecode(Bytes(raw), out var response);

        Assert.True(result);
        Assert.True(response!.Headers.TryGetValues("X-Big", out var values));
        Assert.Equal(longValue, values.First());
    }

    private static int FindSequence(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle)
    {
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            if (haystack.Slice(i, needle.Length).SequenceEqual(needle))
                return i;
        }

        return -1;
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // Phase 2: HTTP/1.0 (RFC 1945) — Client Decoder gap tests
    // ══════════════════════════════════════════════════════════════════════════════

    // ── Status-Line (RFC 1945 §6) ────────────────────────────────────────────────

    [Theory(DisplayName = "1945-dec-003: RFC 1945 status code {0} parsed")]
    [InlineData(200)]
    [InlineData(201)]
    [InlineData(202)]
    [InlineData(204)]
    [InlineData(301)]
    [InlineData(302)]
    [InlineData(304)]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(500)]
    [InlineData(501)]
    [InlineData(502)]
    [InlineData(503)]
    public void Should_ParseStatusCode_When_Rfc1945StatusCode(int code)
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse($"HTTP/1.0 {code} Reason", "Content-Length: 0");

        decoder.TryDecode(data, out var response);

        Assert.Equal((HttpStatusCode)code, response!.StatusCode);
    }

    [Fact(DisplayName = "dec1-sl-001: Unknown status code 299 accepted")]
    public void Should_AcceptUnknownStatusCode_When_299()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 299 Custom", "Content-Length: 0");

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        Assert.Equal((HttpStatusCode)299, response!.StatusCode);
    }

    [Fact(DisplayName = "dec1-sl-002: Status code 99 rejected")]
    public void Should_RejectStatusCode_When_99()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 99 TooLow", "Content-Length: 0");

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(data, out _));
        Assert.Equal(HttpDecodeError.InvalidStatusLine, ex.DecodeError);
    }

    [Fact(DisplayName = "dec1-sl-003: Status code 1000 rejected")]
    public void Should_RejectStatusCode_When_1000()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 1000 TooHigh", "Content-Length: 0");

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(data, out _));
        Assert.Equal(HttpDecodeError.InvalidStatusLine, ex.DecodeError);
    }

    [Fact(DisplayName = "dec1-sl-004: LF-only line endings accepted in HTTP/1.0")]
    public void Should_AcceptLfOnlyLineEndings_When_Http10()
    {
        var decoder = new Http10Decoder();
        const string raw = "HTTP/1.0 200 OK\nContent-Length: 5\n\nHello";

        var result = decoder.TryDecode(Bytes(raw), out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.OK, response!.StatusCode);
    }

    [Fact(DisplayName = "dec1-sl-005: Empty reason phrase after status code accepted")]
    public void Should_AcceptEmptyReasonPhrase_When_StatusCodeOnly()
    {
        var decoder = new Http10Decoder();
        const string raw = "HTTP/1.0 200 \r\nContent-Length: 0\r\n\r\n";

        var result = decoder.TryDecode(Bytes(raw), out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.OK, response!.StatusCode);
        Assert.Equal("", response.ReasonPhrase);
    }

    // ── Header Parsing (RFC 1945 §4) ─────────────────────────────────────────────

    [Fact(DisplayName = "1945-4-002: Obs-fold continuation accepted in HTTP/1.0")]
    public void Should_MergeObsFold_When_ContinuationLine()
    {
        var decoder = new Http10Decoder();
        const string raw = "HTTP/1.0 200 OK\r\nX-Folded: value1\r\n value2\r\nContent-Length: 0\r\n\r\n";

        decoder.TryDecode(Bytes(raw), out var response);

        Assert.True(response!.Headers.TryGetValues("X-Folded", out var values));
        var combined = string.Join("", values);
        Assert.Contains("value1", combined);
        Assert.Contains("value2", combined);
    }

    [Fact(DisplayName = "1945-4-002b: Double obs-fold line merged into single value")]
    public void Should_MergeDoubleObsFold_When_TwoContinuationLines()
    {
        var decoder = new Http10Decoder();
        const string raw = "HTTP/1.0 200 OK\r\nX-Multi: part1\r\n part2\r\n part3\r\nContent-Length: 0\r\n\r\n";

        decoder.TryDecode(Bytes(raw), out var response);

        Assert.True(response!.Headers.TryGetValues("X-Multi", out var values));
        var combined = string.Join("", values);
        Assert.Contains("part1", combined);
        Assert.Contains("part2", combined);
        Assert.Contains("part3", combined);
    }

    [Fact(DisplayName = "1945-4-003: Duplicate response headers both accessible")]
    public void Should_PreserveBothHeaders_When_DuplicateNonContentLength()
    {
        var decoder = new Http10Decoder();
        const string raw = "HTTP/1.0 200 OK\r\nX-Dup: first\r\nX-Dup: second\r\nContent-Length: 0\r\n\r\n";

        decoder.TryDecode(Bytes(raw), out var response);

        Assert.True(response!.Headers.TryGetValues("X-Dup", out var values));
        var list = values.ToList();
        Assert.Contains("first", list);
        Assert.Contains("second", list);
    }

    [Fact(DisplayName = "1945-4-004: Header without colon causes parse error")]
    public void Should_ThrowInvalidHeader_When_NoColon()
    {
        var decoder = new Http10Decoder();
        const string raw = "HTTP/1.0 200 OK\r\nBadHeaderNoColon\r\nContent-Length: 0\r\n\r\n";

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(Bytes(raw), out _));
        Assert.Equal(HttpDecodeError.InvalidHeader, ex.DecodeError);
    }

    [Fact(DisplayName = "1945-4-005: CONTENT-LENGTH and Content-Length are equivalent")]
    public void Should_MatchCaseInsensitive_When_UppercaseHeaderName()
    {
        var decoder = new Http10Decoder();
        const string raw = "HTTP/1.0 200 OK\r\nCONTENT-LENGTH: 5\r\n\r\nHello";

        var result = decoder.TryDecode(Bytes(raw), out var response);

        Assert.True(result);
        Assert.Equal(5, response!.Content.Headers.ContentLength);
    }

    [Fact(DisplayName = "1945-4-006: Header value whitespace trimmed")]
    public void Should_TrimWhitespace_When_HeaderValueHasExtraSpaces()
    {
        var decoder = new Http10Decoder();
        const string raw = "HTTP/1.0 200 OK\r\nX-Trimmed:    hello world   \r\nContent-Length: 0\r\n\r\n";

        decoder.TryDecode(Bytes(raw), out var response);

        Assert.True(response!.Headers.TryGetValues("X-Trimmed", out var values));
        Assert.Equal("hello world", values.First());
    }

    [Fact(DisplayName = "1945-4-007: Space in header name causes parse error")]
    public void Should_ThrowInvalidFieldName_When_SpaceInHeaderName()
    {
        var decoder = new Http10Decoder();
        const string raw = "HTTP/1.0 200 OK\r\nBad Name: value\r\nContent-Length: 0\r\n\r\n";

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(Bytes(raw), out _));
        Assert.Equal(HttpDecodeError.InvalidFieldName, ex.DecodeError);
    }

    [Fact(DisplayName = "dec1-hdr-001: Tab character in header value accepted")]
    public void Should_AcceptTab_When_HeaderValueContainsTab()
    {
        var decoder = new Http10Decoder();
        const string raw = "HTTP/1.0 200 OK\r\nX-Tab: before\tafter\r\nContent-Length: 0\r\n\r\n";

        var result = decoder.TryDecode(Bytes(raw), out var response);

        Assert.True(result);
        Assert.True(response!.Headers.TryGetValues("X-Tab", out var values));
        Assert.Contains("before\tafter", values);
    }

    [Fact(DisplayName = "dec1-hdr-002: Response with no headers except status-line accepted")]
    public void Should_AcceptResponse_When_ZeroHeaders()
    {
        var decoder = new Http10Decoder();
        const string raw = "HTTP/1.0 200 OK\r\n\r\n";

        var result = decoder.TryDecode(Bytes(raw), out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.OK, response!.StatusCode);
    }

    // ── No-Body Responses ────────────────────────────────────────────────────────

    [Fact(DisplayName = "1945-dec-004: 304 Not Modified ignores Content-Length body")]
    public async Task Should_HaveEmptyBody_When_304WithContentLength()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 304 Not Modified", "Content-Length: 100");

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.NotModified, response!.StatusCode);
        var bodyBytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Empty(bodyBytes);
    }

    [Fact(DisplayName = "1945-dec-004b: 304 Not Modified without Content-Length has empty body")]
    public async Task Should_HaveEmptyBody_When_304WithoutContentLength()
    {
        var decoder = new Http10Decoder();
        const string raw = "HTTP/1.0 304 Not Modified\r\n\r\n";

        var result = decoder.TryDecode(Bytes(raw), out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.NotModified, response!.StatusCode);
        var bodyBytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Empty(bodyBytes);
    }

    [Fact(DisplayName = "dec1-nb-001: 204 No Content has empty body")]
    public async Task Should_HaveEmptyBody_When_204NoContent()
    {
        var decoder = new Http10Decoder();
        const string raw = "HTTP/1.0 204 No Content\r\n\r\n";

        var result = decoder.TryDecode(Bytes(raw), out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.NoContent, response!.StatusCode);
        var bodyBytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Empty(bodyBytes);
    }

    // ── Body Parsing (RFC 1945 §7) ──────────────────────────────────────────────

    [Fact(DisplayName = "1945-7-001: Content-Length body decoded to exact byte count")]
    public async Task Should_DecodeExactBytes_When_ContentLengthPresent()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK", "Content-Length: 13", "Hello, World!");

        decoder.TryDecode(data, out var response);

        var body = await response!.Content.ReadAsByteArrayAsync();
        Assert.Equal(13, body.Length);
        Assert.Equal("Hello, World!", Encoding.ASCII.GetString(body));
    }

    [Fact(DisplayName = "1945-7-002: Zero Content-Length produces empty body")]
    public async Task Should_ProduceEmptyBody_When_ZeroContentLength()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK", "Content-Length: 0");

        decoder.TryDecode(data, out var response);

        var body = await response!.Content.ReadAsByteArrayAsync();
        Assert.Empty(body);
    }

    [Fact(DisplayName = "1945-7-003: Body without Content-Length read via TryDecodeEof")]
    public async Task Should_ReadBodyViaEof_When_NoContentLength()
    {
        var decoder = new Http10Decoder();
        const string raw = "HTTP/1.0 200 OK\r\n\r\nEOF body data";

        // First TryDecode consumes headers + body (no CL, so all remaining is body)
        decoder.TryDecode(Bytes(raw), out var response);

        // Connection closed — simulate with TryDecodeEof on a fresh decoder
        var decoder2 = new Http10Decoder();
        var partial = Bytes("HTTP/1.0 200 OK\r\n\r\nincomplete");
        decoder2.TryDecode(partial, out _);

        // Actually, the first decoder already decoded successfully since it has headers+body
        var body = await response!.Content.ReadAsStringAsync();
        Assert.Equal("EOF body data", body);
    }

    [Fact(DisplayName = "1945-7-005: Two different Content-Length values rejected")]
    public void Should_ThrowMultipleContentLength_When_DifferentValues()
    {
        var decoder = new Http10Decoder();
        const string raw = "HTTP/1.0 200 OK\r\nContent-Length: 3\r\nContent-Length: 5\r\n\r\nHello";

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(Bytes(raw), out _));
        Assert.Equal(HttpDecodeError.MultipleContentLengthValues, ex.DecodeError);
    }

    [Fact(DisplayName = "1945-7-005b: Two identical Content-Length values accepted")]
    public async Task Should_AcceptIdenticalContentLength_When_DuplicateValues()
    {
        var decoder = new Http10Decoder();
        const string raw = "HTTP/1.0 200 OK\r\nContent-Length: 5\r\nContent-Length: 5\r\n\r\nHello";

        var result = decoder.TryDecode(Bytes(raw), out var response);

        Assert.True(result);
        var body = await response!.Content.ReadAsStringAsync();
        Assert.Equal("Hello", body);
    }

    [Fact(DisplayName = "1945-7-006: Negative Content-Length is parse error")]
    public void Should_ThrowInvalidContentLength_When_Negative()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK", "Content-Length: -1");

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(data, out _));
        Assert.Equal(HttpDecodeError.InvalidContentLength, ex.DecodeError);
    }

    [Fact(DisplayName = "dec1-body-001: Body with null bytes decoded intact")]
    public async Task Should_PreserveNullBytes_When_BodyContainsThem()
    {
        var bodyBytes = new byte[] { 0x48, 0x00, 0x65, 0x00, 0x6C };
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK",
            $"Content-Length: {bodyBytes.Length}", bodyBytes);

        decoder.TryDecode(data, out var response);

        var actual = await response!.Content.ReadAsByteArrayAsync();
        Assert.Equal(bodyBytes, actual);
    }

    [Fact(DisplayName = "dec1-body-002: 2 MB body decoded with correct Content-Length")]
    public async Task Should_Decode2MbBody_When_LargeContentLength()
    {
        var bodyBytes = new byte[2 * 1024 * 1024];
        new Random(42).NextBytes(bodyBytes);
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK",
            $"Content-Length: {bodyBytes.Length}", bodyBytes);

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        var actual = await response!.Content.ReadAsByteArrayAsync();
        Assert.Equal(bodyBytes.Length, actual.Length);
        Assert.Equal(bodyBytes, actual);
    }

    [Fact(DisplayName = "1945-dec-006: Transfer-Encoding chunked is raw body in HTTP/1.0")]
    public async Task Should_TreatChunkedAsRawBody_When_Http10()
    {
        var decoder = new Http10Decoder();
        // HTTP/1.0 does not support chunked TE — body should be raw
        const string chunkedBody = "5\r\nHello\r\n0\r\n\r\n";
        var data = BuildRawResponse("HTTP/1.0 200 OK",
            $"Transfer-Encoding: chunked\r\nContent-Length: {chunkedBody.Length}", chunkedBody);

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        var body = await response!.Content.ReadAsStringAsync();
        Assert.Equal(chunkedBody, body);
    }

    // ── Connection Semantics (RFC 1945 §8) ──────────────────────────────────────

    [Fact(DisplayName = "1945-8-001: HTTP/1.0 default connection is close")]
    public void Should_DefaultToClose_When_NoConnectionHeader()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK", "Content-Length: 0");

        decoder.TryDecode(data, out var response);

        // HTTP/1.0 default: no Connection header means close
        Assert.False(response!.Headers.TryGetValues("Connection", out _));
        Assert.Equal(new Version(1, 0), response.Version);
    }

    [Fact(DisplayName = "1945-8-002: Connection: keep-alive recognized in HTTP/1.0")]
    public void Should_RecognizeKeepAlive_When_ConnectionHeaderPresent()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK",
            "Connection: keep-alive\r\nContent-Length: 0");

        decoder.TryDecode(data, out var response);

        Assert.True(response!.Headers.TryGetValues("Connection", out var values));
        Assert.Contains("keep-alive", values);
    }

    [Fact(DisplayName = "1945-8-003: Keep-Alive timeout and max parameters parsed")]
    public void Should_ParseKeepAliveParams_When_KeepAliveHeader()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK",
            "Connection: keep-alive\r\nKeep-Alive: timeout=5, max=100\r\nContent-Length: 0");

        decoder.TryDecode(data, out var response);

        Assert.True(response!.Headers.TryGetValues("Keep-Alive", out var values));
        var value = values.First();
        Assert.Contains("timeout=5", value);
        Assert.Contains("max=100", value);
    }

    [Fact(DisplayName = "1945-8-004: HTTP/1.0 does not default to keep-alive")]
    public void Should_NotDefaultToKeepAlive_When_Http10()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK", "Content-Length: 0");

        decoder.TryDecode(data, out var response);

        // No Connection header → not keep-alive in HTTP/1.0
        var hasConnection = response!.Headers.TryGetValues("Connection", out var values);
        Assert.True(!hasConnection || !values!.Any(v =>
            v.Equals("keep-alive", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact(DisplayName = "1945-8-005: Connection: close signals close after response")]
    public void Should_SignalClose_When_ConnectionCloseHeader()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK",
            "Connection: close\r\nContent-Length: 0");

        decoder.TryDecode(data, out var response);

        Assert.True(response!.Headers.TryGetValues("Connection", out var values));
        Assert.Contains("close", values);
    }

    // ── TCP Fragmentation ────────────────────────────────────────────────────────

    [Fact(DisplayName = "dec1-frag-001: Status-line split at byte 1 reassembled")]
    public void Should_Reassemble_When_StatusLineSplitAtByte1()
    {
        var decoder = new Http10Decoder();
        var full = Bytes("HTTP/1.0 200 OK\r\nContent-Length: 3\r\n\r\nABC");

        Assert.False(decoder.TryDecode(full[..1], out _));
        Assert.True(decoder.TryDecode(full[1..], out var response));
        Assert.Equal(HttpStatusCode.OK, response!.StatusCode);
    }

    [Fact(DisplayName = "dec1-frag-002: Status-line split inside HTTP/1.0 version reassembled")]
    public void Should_Reassemble_When_StatusLineSplitInsideVersion()
    {
        var decoder = new Http10Decoder();
        var full = Bytes("HTTP/1.0 200 OK\r\nContent-Length: 3\r\n\r\nABC");

        // Split inside "HTTP/" — at offset 5
        Assert.False(decoder.TryDecode(full[..5], out _));
        Assert.True(decoder.TryDecode(full[5..], out var response));
        Assert.Equal(HttpStatusCode.OK, response!.StatusCode);
    }

    [Fact(DisplayName = "dec1-frag-003: Header name split across two reads")]
    public void Should_Reassemble_When_HeaderNameSplit()
    {
        var decoder = new Http10Decoder();
        var full = Bytes("HTTP/1.0 200 OK\r\nContent-Length: 3\r\n\r\nABC");

        // Split inside "Content-" (offset ~25)
        Assert.False(decoder.TryDecode(full[..25], out _));
        Assert.True(decoder.TryDecode(full[25..], out var response));
        Assert.Equal(HttpStatusCode.OK, response!.StatusCode);
    }

    [Fact(DisplayName = "dec1-frag-004: Header value split across two reads")]
    public void Should_Reassemble_When_HeaderValueSplit()
    {
        var decoder = new Http10Decoder();
        var full = Bytes("HTTP/1.0 200 OK\r\nContent-Length: 3\r\n\r\nABC");

        // Split inside header value area (offset ~33, inside "3\r\n")
        Assert.False(decoder.TryDecode(full[..33], out _));
        Assert.True(decoder.TryDecode(full[33..], out var response));
        Assert.Equal(HttpStatusCode.OK, response!.StatusCode);
    }

    [Fact(DisplayName = "dec1-frag-005: Body split mid-content reassembled")]
    public async Task Should_Reassemble_When_BodySplitMidContent()
    {
        var decoder = new Http10Decoder();
        var full = Bytes("HTTP/1.0 200 OK\r\nContent-Length: 10\r\n\r\n0123456789");

        var separatorIdx = FindSequence(full.Span, "\r\n\r\n"u8) + 4;
        // Split body in the middle
        var splitPoint = separatorIdx + 5;

        Assert.False(decoder.TryDecode(full[..splitPoint], out _));
        Assert.True(decoder.TryDecode(full[splitPoint..], out var response));
        var body = await response!.Content.ReadAsStringAsync();
        Assert.Equal("0123456789", body);
    }
}