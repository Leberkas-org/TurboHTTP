using System.Text;
using TurboHttp.Protocol;
using TurboHttp.Protocol.RFC1945;
using TurboHttp.Protocol.RFC9112;

namespace TurboHttp.Tests.Security;

/// <summary>
/// Tests header injection and HTTP request smuggling prevention across all HTTP/1.x encoders and decoders.
/// Verifies that CRLF injection, NUL byte injection, and Content-Length/Transfer-Encoding conflicts
/// are detected and rejected per RFC 9112 §11 and RFC 9110 §17.
/// </summary>
/// <remarks>
/// Classes under test: <see cref="Http10Encoder"/>, <see cref="Http11Encoder"/>, <see cref="Http11Decoder"/>.
/// Attack vectors: header injection via CR/LF/NUL in names and values, request smuggling via
/// Content-Length/Transfer-Encoding desync and duplicate Content-Length ambiguity.
/// </remarks>
public sealed class HeaderInjectionTests
{
    // ── Encoder helpers ──────────────────────────────────────────────────────────

    private static string EncodeHttp11(HttpRequestMessage request, int bufferSize = 16384)
    {
        var buffer = new Memory<byte>(new byte[bufferSize]);
        var span = buffer.Span;
        var written = Http11Encoder.Encode(request, ref span);
        return Encoding.ASCII.GetString(buffer.Span[..written]);
    }

    private static void EncodeHttp11Throwing(HttpRequestMessage request)
    {
        var buffer = new Memory<byte>(new byte[8192]);
        var span = buffer.Span;
        Http11Encoder.Encode(request, ref span);
    }

    private static void EncodeHttp10Throwing(HttpRequestMessage request)
    {
        var buffer = new Memory<byte>(new byte[8192]);
        Http10Encoder.Encode(request, ref buffer);
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // CRLF Injection in Request Header Names
    // ══════════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "SEC-HDR-INJ-001: CRLF in header name rejected — header line splitting attack")]
    public void Should_RejectHeaderName_When_ContainsCrLf()
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

    [Fact(DisplayName = "SEC-HDR-INJ-002: CR in header name rejected — bare CR splitting attack")]
    public void Should_RejectHeaderName_When_ContainsCr()
    {
        // Attack: Bare CR in header name could cause line splitting in lenient parsers.
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("X-Evil\rInjected", "attack");

        // .NET's HttpRequestHeaders rejects CR in header names
        Assert.DoesNotContain(request.Headers, h => h.Key.Contains("Evil"));
    }

    [Fact(DisplayName = "SEC-HDR-INJ-003: LF in header name rejected — bare LF splitting attack")]
    public void Should_RejectHeaderName_When_ContainsLf()
    {
        // Attack: Bare LF in header name could cause line splitting in lenient parsers.
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("X-Evil\nInjected", "attack");

        // .NET's HttpRequestHeaders rejects LF in header names
        Assert.DoesNotContain(request.Headers, h => h.Key.Contains("Evil"));
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // CRLF Injection in Request Header Values
    // ══════════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "SEC-HDR-INJ-004: CRLF in header value rejected by HTTP/1.1 encoder — response splitting attack")]
    public void Should_RejectHeaderValue_When_ContainsCrLf_Http11()
    {
        // Attack: CRLF in header value creates new header lines.
        // "value\r\nX-Injected: evil" would inject a second header.
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("X-Test", "value\r\nX-Injected: evil");

        Assert.Throws<ArgumentException>(() => EncodeHttp11Throwing(request));
    }

    [Fact(DisplayName = "SEC-HDR-INJ-005: CRLF in header value rejected by HTTP/1.0 encoder — response splitting attack")]
    public void Should_RejectHeaderValue_When_ContainsCrLf_Http10()
    {
        // Same attack vector against HTTP/1.0 encoder
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("X-Test", "value\r\nX-Injected: evil");

        Assert.Throws<ArgumentException>(() => EncodeHttp10Throwing(request));
    }

    [Fact(DisplayName = "SEC-HDR-INJ-006: CR in header value rejected by HTTP/1.1 encoder — bare CR injection")]
    public void Should_RejectHeaderValue_When_ContainsCr_Http11()
    {
        // Attack: Bare CR could be interpreted as line terminator by some proxies.
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("X-Test", "hello\rworld");

        Assert.Throws<ArgumentException>(() => EncodeHttp11Throwing(request));
    }

    [Fact(DisplayName = "SEC-HDR-INJ-007: LF in header value rejected by HTTP/1.1 encoder — bare LF injection")]
    public void Should_RejectHeaderValue_When_ContainsLf_Http11()
    {
        // Attack: Bare LF could be interpreted as line terminator by lenient parsers.
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("X-Test", "hello\nworld");

        Assert.Throws<ArgumentException>(() => EncodeHttp11Throwing(request));
    }

    [Fact(DisplayName = "SEC-HDR-INJ-008: CRLF in content header value rejected by HTTP/1.1 encoder — body header injection")]
    public void Should_RejectContentHeaderValue_When_ContainsCrLf_Http11()
    {
        // Attack: Injecting CRLF in content headers (e.g., Content-Disposition) to inject additional headers.
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/");
        request.Content = new ByteArrayContent("body"u8.ToArray());
        request.Content.Headers.TryAddWithoutValidation("Content-Disposition", "attachment\r\nX-Injected: evil");

        Assert.Throws<ArgumentException>(() => EncodeHttp11Throwing(request));
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // NUL Byte Injection in Headers
    // ══════════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "SEC-HDR-INJ-009: NUL byte in header value rejected by HTTP/1.1 encoder — NUL truncation attack")]
    public void Should_RejectHeaderValue_When_ContainsNul_Http11()
    {
        // Attack: NUL byte can truncate strings in C-based intermediaries,
        // causing the visible value to differ from the transmitted value.
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("X-Test", "safe\0evil");

        Assert.Throws<ArgumentException>(() => EncodeHttp11Throwing(request));
    }

    [Fact(DisplayName = "SEC-HDR-INJ-010: NUL byte in header value rejected by HTTP/1.0 encoder — NUL truncation attack")]
    public void Should_RejectHeaderValue_When_ContainsNul_Http10()
    {
        // Same NUL truncation attack against HTTP/1.0 encoder
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("X-Test", "safe\0evil");

        Assert.Throws<ArgumentException>(() => EncodeHttp10Throwing(request));
    }

    [Fact(DisplayName = "SEC-HDR-INJ-011: NUL byte in decoded response header value rejected — server-sent NUL injection")]
    public void Should_RejectResponse_When_DecodedHeaderValueContainsNul()
    {
        // Attack: Malicious server sends NUL byte in header value.
        // The decoder must reject this per RFC 9112 §5.5.
        var decoder = new Http11Decoder();
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

    // ══════════════════════════════════════════════════════════════════════════════
    // Header Name with Spaces
    // ══════════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "SEC-HDR-INJ-012: Space in header name rejected by HTTP/1.0 decoder — header name confusion attack")]
    public void Should_RejectResponse_When_HeaderNameContainsSpace_Http10()
    {
        // Attack: Space in header name can cause different parsers to interpret
        // the header name boundary differently (e.g., "Content Length" vs "Content").
        var decoder = new Http10Decoder();
        var raw = "HTTP/1.0 200 OK\r\nContent Length: 0\r\n\r\n";
        var bytes = Encoding.ASCII.GetBytes(raw);

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(bytes, out _));
        Assert.Equal(HttpDecoderError.InvalidFieldName, ex.DecodeError);
    }

    [Fact(DisplayName = "SEC-HDR-INJ-013: Space in header name rejected by API — .NET blocks spaces in header names")]
    public void Should_PreventSpaceInHeaderName_When_AddingViaApi()
    {
        // Verify the .NET API itself prevents space-containing header names from reaching the encoder.
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("X Bad Header", "value");

        // .NET rejects header names with spaces
        Assert.DoesNotContain(request.Headers, h => h.Key == "X Bad Header");
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // Header Value with Bare CR (without LF)
    // ══════════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "SEC-HDR-INJ-014: Bare CR in response header value rejected by decoder — CR-only line splitting")]
    public void Should_RejectResponse_When_HeaderValueContainsBareCr()
    {
        // Attack: Some parsers treat bare CR as a line terminator, which could
        // allow header injection if the upstream proxy accepts bare-CR termination.
        var decoder = new Http11Decoder();

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

    [Fact(DisplayName = "SEC-HDR-INJ-015: Bare CR in request header value rejected by encoder — outbound CR injection")]
    public void Should_RejectRequest_When_HeaderValueContainsBareCr()
    {
        // Attack: Ensure the encoder also prevents bare CR from being emitted on the wire.
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("X-Test", "hello\rworld");

        var ex = Assert.Throws<ArgumentException>(() => EncodeHttp11Throwing(request));
        Assert.Contains("X-Test", ex.Message);
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // HTTP/1.1 Request Smuggling: Content-Length + Transfer-Encoding Conflict
    // ══════════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "SEC-SMUG-001: TE+CL conflict in response rejected — CL-TE desync smuggling")]
    public void Should_RejectResponse_When_TransferEncodingAndContentLengthBothPresent()
    {
        // Attack: CL-TE desync — a reverse proxy uses Content-Length to determine
        // body boundary while the backend uses Transfer-Encoding: chunked.
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

    [Fact(DisplayName = "SEC-SMUG-002: TE+CL conflict with reversed header order rejected — TE-CL desync smuggling")]
    public void Should_RejectResponse_When_ContentLengthBeforeTransferEncoding()
    {
        // Attack: Same desync but with headers in reversed order.
        var decoder = new Http11Decoder();
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

    [Fact(DisplayName = "SEC-SMUG-003: TE+CL conflict with CL=0 rejected — zero-length CL-TE desync")]
    public void Should_RejectResponse_When_ChunkedWithContentLengthZero()
    {
        // Attack: Even Content-Length: 0 with Transfer-Encoding: chunked is ambiguous.
        var decoder = new Http11Decoder();
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

    // ══════════════════════════════════════════════════════════════════════════════
    // HTTP/1.1 Request Smuggling: Duplicate Content-Length
    // ══════════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "SEC-SMUG-004: Duplicate CL with different values rejected — CL-CL desync smuggling")]
    public void Should_RejectResponse_When_DuplicateContentLengthDifferentValues()
    {
        // Attack: Two Content-Length headers with different values. A front-end proxy
        // might use the first (5), while the backend uses the second (10).
        var decoder = new Http11Decoder();
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

    [Fact(DisplayName = "SEC-SMUG-005: Duplicate CL with same values accepted — no smuggling ambiguity")]
    public async Task Should_AcceptResponse_When_DuplicateContentLengthSameValues()
    {
        // Non-attack: Duplicate Content-Length with identical values is safe per RFC 9112 §6.3.
        var decoder = new Http11Decoder();
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
        var body = await responses[0].Content.ReadAsByteArrayAsync();
        Assert.Equal("Hello"u8.ToArray(), body);
    }

    [Fact(DisplayName = "SEC-SMUG-006: Three CL headers with last conflicting rejected — multi-value CL desync")]
    public void Should_RejectResponse_When_ThreeConflictingContentLengthValues()
    {
        // Attack: Three Content-Length headers where only the last differs.
        var decoder = new Http11Decoder();
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

    // ══════════════════════════════════════════════════════════════════════════════
    // HTTP/1.1 Encoder: Smugglable Header Prevention
    // ══════════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "SEC-SMUG-007: Encoder omits CL when chunked is set — prevents CL+TE on wire")]
    public void Should_OmitContentLength_When_TransferEncodingChunkedIsSet()
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

    [Fact(DisplayName = "SEC-SMUG-008: Encoder filters hop-by-hop headers — prevents proxy confusion")]
    public void Should_FilterConnectionSpecificHeaders_When_Encoding()
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

    [Fact(DisplayName = "SEC-SMUG-009: Encoder strips chunked from TE header — prevents TE smuggling")]
    public void Should_StripChunkedFromTeHeader_When_Encoding()
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

    [Fact(DisplayName = "SEC-SMUG-010: Encoder produces no bare CR or LF in output — wire-level injection prevention")]
    public void Should_NotEmitBareCrOrLf_When_EncodingNormalRequest()
    {
        // Verify the encoded output uses only CRLF line terminators, never bare CR or LF.
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/path?query=value");
        request.Headers.TryAddWithoutValidation("X-Custom", "safe-value");
        request.Headers.TryAddWithoutValidation("Accept", "text/html");

        var buffer = new Memory<byte>(new byte[16384]);
        var span = buffer.Span;
        var bytesWritten = Http11Encoder.Encode(request, ref span);

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

    // ══════════════════════════════════════════════════════════════════════════════
    // Positive tests: Legitimate headers must work correctly
    // ══════════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "SEC-HDR-INJ-016: Normal header values pass validation — no false positives")]
    public void Should_NotThrow_When_HeaderValuesAreLegitimate()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("X-Request-Id", "abc-123-def-456");
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer eyJhbGciOiJIUzI1NiJ9.token.sig");

        var ex = Record.Exception(() => EncodeHttp11Throwing(request));
        Assert.Null(ex);
    }

    [Fact(DisplayName = "SEC-HDR-INJ-017: Headers with special but safe characters pass — colons, semicolons, equals")]
    public void Should_NotThrow_When_HeaderValuesContainSafeSpecialChars()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("Cookie", "session=abc; path=/; domain=.example.com");
        request.Headers.TryAddWithoutValidation("Accept", "text/html; charset=utf-8");

        var ex = Record.Exception(() => EncodeHttp11Throwing(request));
        Assert.Null(ex);
    }
}
