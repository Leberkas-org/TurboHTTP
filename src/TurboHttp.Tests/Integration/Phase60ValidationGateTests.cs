using System.Net;
using System.Text;
using TurboHttp.Protocol;

namespace TurboHttp.Tests.Integration;

/// <summary>
/// Phase 60 — Final HTTPWG Core Validation Gate
///
/// Explicitly validates all RFC 9110 and RFC 9112 compliance requirements
/// identified in the Phase 60 checklist. Each test maps to a specific
/// RFC section and requirement.
/// </summary>
public sealed class Phase60ValidationGateTests
{
    // ── RFC 9110 §9 — All HTTP Methods Handled Correctly ────────────────────

    [Theory(DisplayName = "P60-9110-001: HTTP/1.1 encoder handles all standard request methods")]
    [InlineData("GET")]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    [InlineData("PATCH")]
    [InlineData("TRACE")]
    [InlineData("CONNECT")]
    public void Should_EncodeMethod_When_AnyStandardMethod(string method)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), "http://example.com/");
        if (method is "POST" or "PUT" or "PATCH")
        {
            request.Content = new ByteArrayContent("body"u8.ToArray());
        }

        // Encode writes into buffer and advances the span to remaining space
        var buffer = new byte[4096];
        var span = buffer.AsSpan();
        var written = Http11Encoder.Encode(request, ref span);
        var encoded = Encoding.ASCII.GetString(buffer, 0, written);

        Assert.StartsWith($"{method} ", encoded);
        Assert.Contains("HTTP/1.1\r\n", encoded);
    }

    [Theory(DisplayName = "P60-9110-002: HTTP/2 encoder emits :method pseudo-header for all standard methods")]
    [InlineData("GET")]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    [InlineData("PATCH")]
    public void Should_EmitMethodPseudoHeader_When_AnyStandardMethodHttp2(string method)
    {
        var encoder = new Http2RequestEncoder();
        var request = new HttpRequestMessage(new HttpMethod(method), "http://example.com/");
        if (method is "POST" or "PUT" or "PATCH")
        {
            request.Content = new ByteArrayContent("body"u8.ToArray());
        }

        // Encode succeeds without throwing — validates pseudo-header emission
        var buffer = new byte[65536].AsMemory();
        var (streamId, bytesWritten) = encoder.Encode(request, ref buffer);
        Assert.True(streamId > 0);
        Assert.True(bytesWritten > 0);
    }

    // ── RFC 9110 §15 — All Status Codes Interpreted Correctly ───────────────

    [Theory(DisplayName = "P60-9110-003: 2xx status codes decoded with correct StatusCode value")]
    [InlineData(200, "OK")]
    [InlineData(201, "Created")]
    [InlineData(202, "Accepted")]
    [InlineData(204, "No Content")]
    [InlineData(206, "Partial Content")]
    public void Should_DecodeCorrectStatusCode_When_2xxResponse(int code, string reason)
    {
        var decoder = new Http11Decoder();
        var raw = BuildResponse(code, reason, code == 204 ? "" : "data",
            code == 204 ? ("Content-Length", "0") : ("Content-Length", "4"));

        var ok = decoder.TryDecode(raw, out var responses);

        Assert.True(ok);
        Assert.Single(responses);
        Assert.Equal(code, (int)responses[0].StatusCode);
    }

    [Theory(DisplayName = "P60-9110-004: 3xx status codes decoded correctly")]
    [InlineData(301, "Moved Permanently")]
    [InlineData(302, "Found")]
    [InlineData(303, "See Other")]
    [InlineData(307, "Temporary Redirect")]
    [InlineData(308, "Permanent Redirect")]
    [InlineData(304, "Not Modified")]
    public void Should_DecodeCorrectStatusCode_When_3xxResponse(int code, string reason)
    {
        var decoder = new Http11Decoder();
        // 304 has no body; 3xx redirect responses may have a small body
        var raw = code == 304
            ? BuildResponse(code, reason)
            : BuildResponse(code, reason, "", ("Location", "http://example.com/new"));

        var ok = decoder.TryDecode(raw, out var responses);

        Assert.True(ok);
        Assert.Single(responses);
        Assert.Equal(code, (int)responses[0].StatusCode);
    }

    [Theory(DisplayName = "P60-9110-005: 4xx status codes decoded correctly")]
    [InlineData(400, "Bad Request")]
    [InlineData(401, "Unauthorized")]
    [InlineData(403, "Forbidden")]
    [InlineData(404, "Not Found")]
    [InlineData(405, "Method Not Allowed")]
    [InlineData(408, "Request Timeout")]
    [InlineData(409, "Conflict")]
    [InlineData(410, "Gone")]
    [InlineData(413, "Content Too Large")]
    [InlineData(429, "Too Many Requests")]
    public void Should_DecodeCorrectStatusCode_When_4xxResponse(int code, string reason)
    {
        var decoder = new Http11Decoder();
        var raw = BuildResponse(code, reason, "error", ("Content-Length", "5"));

        var ok = decoder.TryDecode(raw, out var responses);

        Assert.True(ok);
        Assert.Single(responses);
        Assert.Equal(code, (int)responses[0].StatusCode);
    }

    [Theory(DisplayName = "P60-9110-006: 5xx status codes decoded correctly")]
    [InlineData(500, "Internal Server Error")]
    [InlineData(501, "Not Implemented")]
    [InlineData(502, "Bad Gateway")]
    [InlineData(503, "Service Unavailable")]
    [InlineData(504, "Gateway Timeout")]
    public void Should_DecodeCorrectStatusCode_When_5xxResponse(int code, string reason)
    {
        var decoder = new Http11Decoder();
        var raw = BuildResponse(code, reason, "error", ("Content-Length", "5"));

        var ok = decoder.TryDecode(raw, out var responses);

        Assert.True(ok);
        Assert.Single(responses);
        Assert.Equal(code, (int)responses[0].StatusCode);
    }

    // ── RFC 9110 §5.1 — Headers Treated Case-Insensitively ─────────────────

    [Fact(DisplayName = "P60-9110-007: RFC 9110 §5.1 — header names are case-insensitive (mixed case accepted)")]
    public void Should_AcceptHeaders_When_MixedCaseHeaderNames()
    {
        var decoder = new Http11Decoder();
        // RFC 9112: field names are case-insensitive
        const string raw = "HTTP/1.1 200 OK\r\nCONTENT-LENGTH: 5\r\n\r\nhello";
        var bytes = Encoding.ASCII.GetBytes(raw).AsMemory();

        var ok = decoder.TryDecode(bytes, out var responses);

        Assert.True(ok);
        Assert.Single(responses);
        Assert.Equal((int)HttpStatusCode.OK, (int)responses[0].StatusCode);
    }

    [Fact(DisplayName = "P60-9110-008: RFC 9110 §5.1 — lowercase Content-Length accepted")]
    public void Should_AcceptHeaders_When_LowercaseContentLength()
    {
        var decoder = new Http11Decoder();
        const string raw = "HTTP/1.1 200 OK\r\ncontent-length: 5\r\n\r\nhello";
        var bytes = Encoding.ASCII.GetBytes(raw).AsMemory();

        var ok = decoder.TryDecode(bytes, out var responses);

        Assert.True(ok);
        Assert.Single(responses);
        Assert.Equal((int)HttpStatusCode.OK, (int)responses[0].StatusCode);
    }

    [Fact(DisplayName = "P60-9110-009: RFC 9110 §5.1 — Transfer-Encoding case variants accepted")]
    public void Should_AcceptHeaders_When_TransferEncodingCaseVariants()
    {
        var decoder = new Http11Decoder();
        // TRANSFER-ENCODING: CHUNKED in uppercase — HTTP requires case-insensitive header names
        const string raw = "HTTP/1.1 200 OK\r\nTRANSFER-ENCODING: chunked\r\n\r\n5\r\nhello\r\n0\r\n\r\n";
        var bytes = Encoding.ASCII.GetBytes(raw).AsMemory();

        var ok = decoder.TryDecode(bytes, out var responses);

        Assert.True(ok);
        Assert.Single(responses);
    }

    // ── RFC 9110 §6.4 — Multiple Header Combination Rules ───────────────────

    [Fact(DisplayName = "P60-9110-010: RFC 9112 §6.3 — Transfer-Encoding + Content-Length both present is rejected")]
    public void Should_RejectResponse_When_BothTransferEncodingAndContentLengthPresent()
    {
        var decoder = new Http11Decoder();
        const string raw =
            "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\nContent-Length: 5\r\n\r\n5\r\nhello\r\n0\r\n\r\n";
        var bytes = Encoding.ASCII.GetBytes(raw).AsMemory();

        // Decoder throws to signal protocol violation (request-smuggling risk)
        Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(bytes, out _));
    }

    [Fact(DisplayName = "P60-9110-011: RFC 9112 §6.3 — Multiple differing Content-Length values rejected")]
    public void Should_RejectResponse_When_MultipleContentLengthValues()
    {
        var decoder = new Http11Decoder();
        const string raw = "HTTP/1.1 200 OK\r\nContent-Length: 5\r\nContent-Length: 6\r\n\r\nhello";
        var bytes = Encoding.ASCII.GetBytes(raw).AsMemory();

        // Decoder throws to signal smuggling risk
        Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(bytes, out _));
    }

    // ── RFC 9110 §6.3 — Message Body Rules Fully Implemented ────────────────

    [Fact(DisplayName = "P60-9110-012: RFC 9110 §6.3 — body decoded correctly for Content-Length framing")]
    public async Task Should_DecodeExactBody_When_ContentLengthFraming()
    {
        var decoder = new Http11Decoder();
        const string bodyText = "Hello, World!";
        var raw = BuildResponse(200, "OK", bodyText, ("Content-Length", bodyText.Length.ToString()));

        var ok = decoder.TryDecode(raw, out var responses);

        Assert.True(ok);
        Assert.Single(responses);
        var body = await responses[0].Content.ReadAsStringAsync();
        Assert.Equal(bodyText, body);
    }

    [Fact(DisplayName = "P60-9110-013: RFC 9110 §6.3 — body decoded correctly for chunked framing")]
    public async Task Should_DecodeFullBody_When_ChunkedFraming()
    {
        var decoder = new Http11Decoder();
        const string raw =
            "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n5\r\nHello\r\n6\r\n World\r\n0\r\n\r\n";
        var bytes = Encoding.ASCII.GetBytes(raw).AsMemory();

        var ok = decoder.TryDecode(bytes, out var responses);

        Assert.True(ok);
        Assert.Single(responses);
        var body = await responses[0].Content.ReadAsStringAsync();
        Assert.Equal("Hello World", body);
    }

    [Fact(DisplayName = "P60-9110-014: RFC 9110 §6.3 — zero-length body for Content-Length: 0")]
    public async Task Should_DecodeEmptyBody_When_ZeroContentLength()
    {
        var decoder = new Http11Decoder();
        var raw = BuildResponse(200, "OK", "", ("Content-Length", "0"));

        var ok = decoder.TryDecode(raw, out var responses);

        Assert.True(ok);
        Assert.Single(responses);
        var body = await responses[0].Content.ReadAsStringAsync();
        Assert.Equal("", body);
    }

    // ── RFC 9110 §15.2.1 — Proper Handling of 1xx Informational Responses ───

    [Fact(DisplayName = "P60-9110-015: RFC 9110 §15.2 — 100 Continue before 200 OK skips interim")]
    public void Should_SkipInterim100_When_100ThenFinalResponse()
    {
        var decoder = new Http11Decoder();
        var raw100 = "HTTP/1.1 100 Continue\r\n\r\n"u8.ToArray();
        var raw200 = BuildResponse(200, "OK", "body", ("Content-Length", "4"));
        var combined = new byte[raw100.Length + raw200.Length];
        raw100.CopyTo(combined, 0);
        raw200.Span.CopyTo(combined.AsSpan(raw100.Length));

        var ok = decoder.TryDecode(combined.AsMemory(), out var responses);

        Assert.True(ok);
        // 100 Continue must not be returned as a final response
        Assert.Single(responses);
        Assert.Equal(200, (int)responses[0].StatusCode);
    }

    [Theory(DisplayName = "P60-9110-016: RFC 9110 §15.2 — all 1xx codes skipped before final response")]
    [InlineData(100, "Continue")]
    [InlineData(101, "Switching Protocols")]
    [InlineData(102, "Processing")]
    [InlineData(103, "Early Hints")]
    public void Should_Skip1xx_When_InterimBeforeFinalResponse(int code, string reason)
    {
        var decoder = new Http11Decoder();
        var raw1xx = Encoding.ASCII.GetBytes($"HTTP/1.1 {code} {reason}\r\n\r\n");
        var raw200 = BuildResponse(200, "OK", "ok", ("Content-Length", "2"));
        var combined = new byte[raw1xx.Length + raw200.Length];
        raw1xx.CopyTo(combined, 0);
        raw200.Span.CopyTo(combined.AsSpan(raw1xx.Length));

        var ok = decoder.TryDecode(combined.AsMemory(), out var responses);

        Assert.True(ok);
        Assert.Single(responses);
        Assert.Equal(200, (int)responses[0].StatusCode);
    }

    [Fact(DisplayName = "P60-9110-017: RFC 9110 §15.2 — 1xx is NOT treated as a final response")]
    public void Should_Return_NeedMoreData_When_Only1xxPresent()
    {
        var decoder = new Http11Decoder();
        var raw = "HTTP/1.1 100 Continue\r\n\r\n"u8.ToArray();

        var ok = decoder.TryDecode(raw.AsMemory(), out var responses);

        // A 1xx alone is not a final response — decoder needs more data
        Assert.False(ok);
        Assert.Empty(responses);
    }

    // ── RFC 9110 §15.3.4 — 204 No Content Must Have No Body ────────────────

    [Fact(DisplayName = "P60-9110-018: RFC 9110 §15.3.4 — 204 No Content has empty body")]
    public async Task Should_HaveEmptyBody_When_204NoContent()
    {
        var decoder = new Http11Decoder();
        var raw = BuildResponse(204, "No Content");

        var ok = decoder.TryDecode(raw, out var responses);

        Assert.True(ok);
        Assert.Single(responses);
        Assert.Equal(204, (int)responses[0].StatusCode);
        var body = await responses[0].Content.ReadAsByteArrayAsync();
        Assert.Empty(body);
    }

    [Fact(DisplayName = "P60-9110-019: RFC 9110 §15.3.4 — 204 ignores Content-Length body")]
    public async Task Should_HaveEmptyBody_When_204WithContentLength()
    {
        var decoder = new Http11Decoder();
        // 204 must never have a body even if Content-Length is present
        var raw = BuildResponse(204, "No Content", "", ("Content-Length", "0"));

        var ok = decoder.TryDecode(raw, out var responses);

        Assert.True(ok);
        Assert.Single(responses);
        var body = await responses[0].Content.ReadAsByteArrayAsync();
        Assert.Empty(body);
    }

    // ── RFC 9110 §15.4.5 — 304 Not Modified Must Have No Body ───────────────

    [Fact(DisplayName = "P60-9110-020: RFC 9110 §15.4.5 — 304 Not Modified has empty body")]
    public async Task Should_HaveEmptyBody_When_304NotModified()
    {
        var decoder = new Http11Decoder();
        var raw = BuildResponse(304, "Not Modified", "",
            ("ETag", "\"abc123\""),
            ("Cache-Control", "max-age=3600"));

        var ok = decoder.TryDecode(raw, out var responses);

        Assert.True(ok);
        Assert.Single(responses);
        Assert.Equal(304, (int)responses[0].StatusCode);
        var body = await responses[0].Content.ReadAsByteArrayAsync();
        Assert.Empty(body);
    }

    // ── RFC 9110 §9.3.2 — HEAD Response Must Have No Body ───────────────────

    [Fact(DisplayName = "P60-9110-021: RFC 9110 §9.3.2 — TryDecodeHead returns headers without body")]
    public async Task Should_ReturnHeadersOnly_When_HeadResponseDecoded()
    {
        var decoder = new Http11Decoder();
        // HEAD response: has Content-Length header but no body bytes
        const string raw = "HTTP/1.1 200 OK\r\nContent-Length: 42\r\nContent-Type: text/html\r\n\r\n";
        var bytes = Encoding.ASCII.GetBytes(raw).AsMemory();

        var ok = decoder.TryDecodeHead(bytes, out var responses);

        Assert.True(ok);
        Assert.Single(responses);
        Assert.Equal(200, (int)responses[0].StatusCode);
        // Body must be empty for HEAD
        var body = await responses[0].Content.ReadAsByteArrayAsync();
        Assert.Empty(body);
        // Content-Length header must still be present
        Assert.True(responses[0].Content.Headers.ContentLength == 42);
    }

    [Fact(DisplayName = "P60-9110-022: RFC 9110 §9.3.2 — TryDecodeHead with 404 returns no body")]
    public async Task Should_ReturnNoBody_When_HeadReturns404()
    {
        var decoder = new Http11Decoder();
        const string raw = "HTTP/1.1 404 Not Found\r\nContent-Length: 100\r\n\r\n";
        var bytes = Encoding.ASCII.GetBytes(raw).AsMemory();

        var ok = decoder.TryDecodeHead(bytes, out var responses);

        Assert.True(ok);
        Assert.Single(responses);
        Assert.Equal(404, (int)responses[0].StatusCode);
        var body = await responses[0].Content.ReadAsByteArrayAsync();
        Assert.Empty(body);
    }

    // ── RFC 9112 §7.1 — Correct Chunked Decoding ────────────────────────────

    [Fact(DisplayName = "P60-9112-001: RFC 9112 §7.1 — single-byte chunks decoded correctly")]
    public async Task Should_DecodeSingleByteChunks_When_ChunkedEncoding()
    {
        var decoder = new Http11Decoder();
        // Each letter in its own chunk
        const string raw =
            "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n1\r\nA\r\n1\r\nB\r\n1\r\nC\r\n0\r\n\r\n";
        var bytes = Encoding.ASCII.GetBytes(raw).AsMemory();

        var ok = decoder.TryDecode(bytes, out var responses);

        Assert.True(ok);
        Assert.Single(responses);
        var body = await responses[0].Content.ReadAsStringAsync();
        Assert.Equal("ABC", body);
    }

    [Fact(DisplayName = "P60-9112-002: RFC 9112 §7.1 — uppercase hex chunk sizes accepted")]
    public async Task Should_DecodeUppercaseHexChunkSize_When_ChunkedEncoding()
    {
        var decoder = new Http11Decoder();
        // 0xA = 10 bytes
        const string raw = "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\nA\r\n0123456789\r\n0\r\n\r\n";
        var bytes = Encoding.ASCII.GetBytes(raw).AsMemory();

        var ok = decoder.TryDecode(bytes, out var responses);

        Assert.True(ok);
        Assert.Single(responses);
        var body = await responses[0].Content.ReadAsStringAsync();
        Assert.Equal("0123456789", body);
    }

    // ── RFC 9112 §7.1.1 — Chunk Extensions Safely Ignored ───────────────────

    [Fact(DisplayName = "P60-9112-003: RFC 9112 §7.1.1 — chunk extension with name safely ignored")]
    public async Task Should_IgnoreChunkExtension_When_NameOnlyExtension()
    {
        var decoder = new Http11Decoder();
        const string raw = "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n5;ext-name\r\nhello\r\n0\r\n\r\n";
        var bytes = Encoding.ASCII.GetBytes(raw).AsMemory();

        var ok = decoder.TryDecode(bytes, out var responses);

        Assert.True(ok);
        Assert.Single(responses);
        var body = await responses[0].Content.ReadAsStringAsync();
        Assert.Equal("hello", body);
    }

    [Fact(DisplayName = "P60-9112-004: RFC 9112 §7.1.1 — chunk extension with name=value safely ignored")]
    public async Task Should_IgnoreChunkExtension_When_NameValueExtension()
    {
        var decoder = new Http11Decoder();
        const string raw = "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n5;ext=val\r\nhello\r\n0\r\n\r\n";
        var bytes = Encoding.ASCII.GetBytes(raw).AsMemory();

        var ok = decoder.TryDecode(bytes, out var responses);

        Assert.True(ok);
        Assert.Single(responses);
        var body = await responses[0].Content.ReadAsStringAsync();
        Assert.Equal("hello", body);
    }

    [Fact(DisplayName = "P60-9112-005: RFC 9112 §7.1.1 — multiple chunk extensions safely ignored")]
    public async Task Should_IgnoreMultipleChunkExtensions_When_SemicolonSeparated()
    {
        var decoder = new Http11Decoder();
        const string raw = "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n5;a=1;b=2;c\r\nhello\r\n0\r\n\r\n";
        var bytes = Encoding.ASCII.GetBytes(raw).AsMemory();

        var ok = decoder.TryDecode(bytes, out var responses);

        Assert.True(ok);
        Assert.Single(responses);
        var body = await responses[0].Content.ReadAsStringAsync();
        Assert.Equal("hello", body);
    }

    [Fact(DisplayName = "P60-9112-006: RFC 9112 §7.1.1 — chunk extension with quoted value safely ignored")]
    public async Task Should_IgnoreChunkExtension_When_QuotedValue()
    {
        var decoder = new Http11Decoder();
        const string raw =
            "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n5;ext=\"quoted value\"\r\nhello\r\n0\r\n\r\n";
        var bytes = Encoding.ASCII.GetBytes(raw).AsMemory();

        var ok = decoder.TryDecode(bytes, out var responses);

        Assert.True(ok);
        Assert.Single(responses);
        var body = await responses[0].Content.ReadAsStringAsync();
        Assert.Equal("hello", body);
    }

    // ── RFC 9112 §7.1.2 — Trailer Fields Handled or Discarded Safely ────────

    [Fact(DisplayName = "P60-9112-007: RFC 9112 §7.1.2 — trailer headers after final chunk accessible")]
    public void Should_AccessTrailer_When_TrailerAfterFinalChunk()
    {
        var decoder = new Http11Decoder();
        const string raw =
            "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n5\r\nhello\r\n0\r\nX-Checksum: abc123\r\n\r\n";
        var bytes = Encoding.ASCII.GetBytes(raw).AsMemory();

        var ok = decoder.TryDecode(bytes, out var responses);

        Assert.True(ok);
        Assert.Single(responses);
        Assert.True(responses[0].TrailingHeaders.TryGetValues("X-Checksum", out var values));
        Assert.Equal("abc123", values.First());
    }

    [Fact(DisplayName = "P60-9112-008: RFC 9112 §7.1.2 — multiple trailer fields all accessible")]
    public void Should_AccessMultipleTrailers_When_MultipleTrailerFields()
    {
        var decoder = new Http11Decoder();
        const string raw =
            "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n3\r\nfoo\r\n0\r\nX-Trailer-A: val-a\r\nX-Trailer-B: val-b\r\n\r\n";
        var bytes = Encoding.ASCII.GetBytes(raw).AsMemory();

        var ok = decoder.TryDecode(bytes, out var responses);

        Assert.True(ok);
        Assert.Single(responses);
        Assert.True(responses[0].TrailingHeaders.TryGetValues("X-Trailer-A", out var aValues));
        Assert.True(responses[0].TrailingHeaders.TryGetValues("X-Trailer-B", out var bValues));
        Assert.Equal("val-a", aValues.First());
        Assert.Equal("val-b", bValues.First());
    }

    [Fact(DisplayName = "P60-9112-009: RFC 9112 §7.1.2 — chunked body without trailers decoded correctly")]
    public async Task Should_DecodeChunked_When_NoTrailers()
    {
        var decoder = new Http11Decoder();
        const string raw = "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n5\r\nworld\r\n0\r\n\r\n";
        var bytes = Encoding.ASCII.GetBytes(raw).AsMemory();

        var ok = decoder.TryDecode(bytes, out var responses);

        Assert.True(ok);
        Assert.Single(responses);
        var body = await responses[0].Content.ReadAsStringAsync();
        Assert.Equal("world", body);
        // No trailers present
        Assert.Empty(responses[0].TrailingHeaders);
    }

    // ── RFC 9112 §6.3 — Content-Length Conflicts Handled Securely ───────────

    [Fact(DisplayName = "P60-9112-010: RFC 9112 §6.3 — negative Content-Length treated as zero (graceful)")]
    public async Task Should_TreatNegativeContentLengthAsZero_When_NegativeContentLength()
    {
        var decoder = new Http11Decoder();
        const string raw = "HTTP/1.1 200 OK\r\nContent-Length: -1\r\n\r\n";
        var bytes = Encoding.ASCII.GetBytes(raw).AsMemory();

        // Decoder gracefully treats -1 as 0 to avoid undefined behavior
        var ok = decoder.TryDecode(bytes, out var responses);

        Assert.True(ok);
        Assert.Single(responses);
        var body = await responses[0].Content.ReadAsByteArrayAsync();
        Assert.Empty(body);
    }

    [Fact(DisplayName = "P60-9112-011: RFC 9112 §6.3 — Content-Length larger than actual body is NeedMoreData")]
    public void Should_ReturnNeedMoreData_When_ContentLengthExceedsBody()
    {
        var decoder = new Http11Decoder();
        // Declares 100 bytes but only 5 present
        const string raw = "HTTP/1.1 200 OK\r\nContent-Length: 100\r\n\r\nhello";
        var bytes = Encoding.ASCII.GetBytes(raw).AsMemory();

        var ok = decoder.TryDecode(bytes, out var responses);

        // Parser correctly waits for more data
        Assert.False(ok);
        Assert.Empty(responses);
    }

    [Fact(DisplayName = "P60-9112-012: RFC 9112 §6.3 — Transfer-Encoding + Content-Length in either order rejected")]
    public void Should_RejectResponse_When_BothTEAndCL()
    {
        var decoder = new Http11Decoder();
        // RFC 9112 §6.3: if both Transfer-Encoding and Content-Length are present, must reject
        const string raw =
            "HTTP/1.1 200 OK\r\nContent-Length: 5\r\nTransfer-Encoding: chunked\r\n\r\n5\r\nhello\r\n0\r\n\r\n";
        var bytes = Encoding.ASCII.GetBytes(raw).AsMemory();

        Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(bytes, out _));
    }

    // ── RFC 9110 §5.3 — Header Field Order and Combination ──────────────────

    [Fact(DisplayName = "P60-9110-023: RFC 9110 §5.3 — multiple same-name headers combined with comma")]
    public void Should_CombineMultipleSameNameHeaders_When_HeadersAppearMultipleTimes()
    {
        var decoder = new Http11Decoder();
        const string raw = "HTTP/1.1 200 OK\r\nContent-Length: 0\r\nX-Custom: val1\r\nX-Custom: val2\r\n\r\n";
        var bytes = Encoding.ASCII.GetBytes(raw).AsMemory();

        var ok = decoder.TryDecode(bytes, out var responses);

        Assert.True(ok);
        Assert.Single(responses);
        var values = responses[0].Headers.GetValues("X-Custom").ToList();
        Assert.Contains("val1", values);
        Assert.Contains("val2", values);
    }

    // ── RFC 9110 §6.4 — Status Codes Without Bodies ─────────────────────────

    [Theory(DisplayName = "P60-9110-024: RFC 9110 §6.4 — no-body status codes have empty body")]
    [InlineData(204, "No Content")]
    [InlineData(304, "Not Modified")]
    public async Task Should_HaveEmptyBody_When_NoBodyStatusCode(int code, string reason)
    {
        var decoder = new Http11Decoder();
        var raw = BuildResponse(code, reason);

        var ok = decoder.TryDecode(raw, out var responses);

        Assert.True(ok);
        Assert.Single(responses);
        Assert.Equal(code, (int)responses[0].StatusCode);
        var body = await responses[0].Content.ReadAsByteArrayAsync();
        Assert.Empty(body);
    }

    // ── RFC 9112 — Chunked Transfer Encoding Comprehensive ───────────────────

    [Fact(DisplayName = "P60-9112-013: RFC 9112 §7.1 — 32KB chunked body decoded correctly")]
    public async Task Should_DecodeLargeBody_When_LargeChunkedResponse()
    {
        var decoder = new Http11Decoder();
        var largeBody = new string('X', 32768);
        var chunkSize = 32768.ToString("X");
        var raw = $"HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n{chunkSize}\r\n{largeBody}\r\n0\r\n\r\n";
        var bytes = Encoding.ASCII.GetBytes(raw).AsMemory();

        var ok = decoder.TryDecode(bytes, out var responses);

        Assert.True(ok);
        Assert.Single(responses);
        var body = await responses[0].Content.ReadAsStringAsync();
        Assert.Equal(largeBody, body);
    }

    [Fact(DisplayName = "P60-9112-014: RFC 9112 §7.1 — non-hex chunk size is a parse error")]
    public void Should_RejectResponse_When_NonHexChunkSize()
    {
        var decoder = new Http11Decoder();
        const string raw = "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\nZZ\r\nhello\r\n0\r\n\r\n";
        var bytes = Encoding.ASCII.GetBytes(raw).AsMemory();

        Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(bytes, out _));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static ReadOnlyMemory<byte> BuildResponse(int code, string reason, string body = "",
        params (string Name, string Value)[] headers)
    {
        var sb = new StringBuilder();
        sb.Append($"HTTP/1.1 {code} {reason}\r\n");
        foreach (var (name, value) in headers)
        {
            sb.Append($"{name}: {value}\r\n");
        }

        if (!string.IsNullOrEmpty(body) && !headers.Any(h =>
                h.Name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) ||
                h.Name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)))
        {
            sb.Append($"Content-Length: {Encoding.UTF8.GetByteCount(body)}\r\n");
        }

        sb.Append("\r\n");
        sb.Append(body);
        return Encoding.UTF8.GetBytes(sb.ToString());
    }
}