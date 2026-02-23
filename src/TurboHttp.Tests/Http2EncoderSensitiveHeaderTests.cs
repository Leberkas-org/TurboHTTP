#nullable enable
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using TurboHttp.Protocol;

namespace TurboHttp.Tests;

/// <summary>
/// Phase 31: Http2Encoder — Sensitive Header Handling (RFC 7541 §7.1.3).
///
/// Verifies that security-sensitive headers (Authorization, Proxy-Authorization,
/// Cookie, Set-Cookie) are encoded with the HPACK NeverIndexed representation
/// (§6.2.3) to prevent compression-based attacks (e.g., CRIME/BREACH).
///
/// Sensitive headers must:
///   1. Be encoded with the 0x1x prefix (NeverIndexed literal, RFC 7541 §6.2.3)
///   2. NEVER be added to the HPACK dynamic table (no caching)
///   3. Preserve their value exactly through encode/decode round-trip
///   4. Be detected regardless of header name casing
/// </summary>
public sealed class Http2EncoderSensitiveHeaderTests
{
    // =========================================================================
    // Category 1: Core Sensitive Headers Are NeverIndexed (8 tests)
    // =========================================================================

    [Fact(DisplayName = "7541-7.1.3-s001: Authorization header encoded as NeverIndexed")]
    public void Should_EncodeAsNeverIndexed_When_AuthorizationHeader()
    {
        var request = MakeGetRequest();
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer token123");

        var decoded = EncodeAndDecodeHeaders(request);
        var header = decoded.First(h => h.Name == "authorization");

        Assert.True(header.NeverIndex, "RFC 7541 §7.1.3: Authorization must be NeverIndexed");
    }

    [Fact(DisplayName = "7541-7.1.3-s002: Proxy-Authorization header encoded as NeverIndexed")]
    public void Should_EncodeAsNeverIndexed_When_ProxyAuthorizationHeader()
    {
        var request = MakeGetRequest();
        request.Headers.TryAddWithoutValidation("Proxy-Authorization", "Basic dXNlcjpwYXNz");

        var decoded = EncodeAndDecodeHeaders(request);
        var header = decoded.First(h => h.Name == "proxy-authorization");

        Assert.True(header.NeverIndex, "RFC 7541 §7.1.3: Proxy-Authorization must be NeverIndexed");
    }

    [Fact(DisplayName = "7541-7.1.3-s003: Cookie header encoded as NeverIndexed")]
    public void Should_EncodeAsNeverIndexed_When_CookieHeader()
    {
        var request = MakeGetRequest();
        request.Headers.TryAddWithoutValidation("Cookie", "session=abc123; token=xyz");

        var decoded = EncodeAndDecodeHeaders(request);
        var header = decoded.First(h => h.Name == "cookie");

        Assert.True(header.NeverIndex, "RFC 7541 §7.1.3: Cookie must be NeverIndexed");
    }

    [Fact(DisplayName = "7541-7.1.3-s004: Set-Cookie header encoded as NeverIndexed")]
    public void Should_EncodeAsNeverIndexed_When_SetCookieHeader()
    {
        var request = MakeGetRequest();
        request.Headers.TryAddWithoutValidation("Set-Cookie", "session=abc; HttpOnly; Secure");

        var decoded = EncodeAndDecodeHeaders(request);
        var header = decoded.First(h => h.Name == "set-cookie");

        Assert.True(header.NeverIndex, "RFC 7541 §7.1.3: Set-Cookie must be NeverIndexed");
    }

    [Fact(DisplayName = "7541-7.1.3-s005: Authorization detection is case-insensitive")]
    public void Should_EncodeAsNeverIndexed_When_AuthorizationHeaderUppercase()
    {
        var request = MakeGetRequest();
        request.Headers.TryAddWithoutValidation("AUTHORIZATION", "Bearer case-test");

        var decoded = EncodeAndDecodeHeaders(request);
        var header = decoded.FirstOrDefault(h => h.Name == "authorization");

        Assert.NotNull(header.Name);
        Assert.True(header.NeverIndex, "NeverIndexed detection must be case-insensitive");
    }

    [Fact(DisplayName = "7541-7.1.3-s006: Authorization with empty value is still NeverIndexed")]
    public void Should_EncodeAsNeverIndexed_When_AuthorizationValueIsEmpty()
    {
        var request = MakeGetRequest();
        request.Headers.TryAddWithoutValidation("Authorization", "");

        var decoded = EncodeAndDecodeHeaders(request);
        var header = decoded.FirstOrDefault(h => h.Name == "authorization");

        Assert.NotNull(header.Name);
        Assert.True(header.NeverIndex);
    }

    [Fact(DisplayName = "7541-7.1.3-s007: Authorization with long value is still NeverIndexed")]
    public void Should_EncodeAsNeverIndexed_When_AuthorizationValueIsLong()
    {
        var longToken = $"Bearer {new string('a', 512)}";
        var request = MakeGetRequest();
        request.Headers.TryAddWithoutValidation("Authorization", longToken);

        var decoded = EncodeAndDecodeHeaders(request);
        var header = decoded.First(h => h.Name == "authorization");

        Assert.True(header.NeverIndex);
        Assert.Equal(longToken, header.Value);
    }

    [Fact(DisplayName = "7541-7.1.3-s008: Cookie with complex multi-part value is still NeverIndexed")]
    public void Should_EncodeAsNeverIndexed_When_CookieHasComplexValue()
    {
        var request = MakeGetRequest();
        request.Headers.TryAddWithoutValidation("Cookie", "session=abc123; userid=42; pref=dark; lang=en");

        var decoded = EncodeAndDecodeHeaders(request);
        var header = decoded.First(h => h.Name == "cookie");

        Assert.True(header.NeverIndex);
    }

    // =========================================================================
    // Category 2: Non-Sensitive Headers Are NOT NeverIndexed (5 tests)
    // =========================================================================

    [Fact(DisplayName = "7541-7.1.3-s009: x-api-key header is NOT NeverIndexed")]
    public void Should_NotBeNeverIndexed_When_XApiKeyHeader()
    {
        var request = MakeGetRequest();
        request.Headers.TryAddWithoutValidation("X-Api-Key", "my-api-key");

        var decoded = EncodeAndDecodeHeaders(request);
        var header = decoded.First(h => h.Name == "x-api-key");

        Assert.False(header.NeverIndex, "x-api-key is not a recognized sensitive header");
    }

    [Fact(DisplayName = "7541-7.1.3-s010: User-Agent header is NOT NeverIndexed")]
    public void Should_NotBeNeverIndexed_When_UserAgentHeader()
    {
        var request = MakeGetRequest();
        request.Headers.TryAddWithoutValidation("User-Agent", "TurboHttp/1.0");

        var decoded = EncodeAndDecodeHeaders(request);
        var header = decoded.First(h => h.Name == "user-agent");

        Assert.False(header.NeverIndex);
    }

    [Fact(DisplayName = "7541-7.1.3-s011: X-Request-Id header is NOT NeverIndexed")]
    public void Should_NotBeNeverIndexed_When_CustomHeader()
    {
        var request = MakeGetRequest();
        request.Headers.TryAddWithoutValidation("X-Request-Id", "req-12345");

        var decoded = EncodeAndDecodeHeaders(request);
        var header = decoded.First(h => h.Name == "x-request-id");

        Assert.False(header.NeverIndex);
    }

    [Fact(DisplayName = "7541-7.1.3-s012: Pseudo-headers (:method, :path, etc.) are NOT NeverIndexed")]
    public void Should_NotBeNeverIndexed_When_PseudoHeaders()
    {
        var request = MakeGetRequest();
        var decoded = EncodeAndDecodeHeaders(request);

        foreach (var header in decoded.Where(h => h.Name.StartsWith(':')))
        {
            Assert.False(header.NeverIndex, $"Pseudo-header {header.Name} should not be NeverIndexed");
        }
    }

    [Fact(DisplayName = "7541-7.1.3-s013: Accept header is NOT NeverIndexed")]
    public void Should_NotBeNeverIndexed_When_AcceptHeader()
    {
        var request = MakeGetRequest();
        request.Headers.Accept.ParseAdd("application/json");

        var decoded = EncodeAndDecodeHeaders(request);
        var header = decoded.First(h => h.Name == "accept");

        Assert.False(header.NeverIndex);
    }

    // =========================================================================
    // Category 3: NeverIndexed Headers NOT Added to Dynamic Table (4 tests)
    // =========================================================================

    [Fact(DisplayName = "7541-7.1.3-s014: Authorization encoded twice produces same-size HPACK output (no caching)")]
    public void Should_NotReduceHpackSize_When_AuthorizationEncodedTwice()
    {
        // Sensitive headers must NOT be added to the dynamic table.
        // Test at HpackEncoder level to isolate authorization from pseudo-header caching noise.
        var encoder = new HpackEncoder(useHuffman: false);
        var headers = new List<(string Name, string Value)>
        {
            ("authorization", "Bearer same-token")
        };

        var block1 = encoder.Encode(headers);
        var block2 = encoder.Encode(headers);

        Assert.Equal(block1.Length, block2.Length);
    }

    [Fact(DisplayName = "7541-7.1.3-s015: Non-sensitive header encoded twice is smaller the second time (caching works)")]
    public void Should_ReduceHpackSize_When_NonSensitiveHeaderEncodedTwice()
    {
        // Verify that IncrementalIndexing headers ARE cached (contrast with NeverIndexed)
        var encoder = new Http2Encoder(useHuffman: false);

        var req1 = MakeGetRequest();
        req1.Headers.TryAddWithoutValidation("X-Custom-Header", "some-stable-value");

        var req2 = MakeGetRequest();
        req2.Headers.TryAddWithoutValidation("X-Custom-Header", "some-stable-value");

        var block1 = ExtractHpackBlockFromEncoder(encoder, req1);
        var block2 = ExtractHpackBlockFromEncoder(encoder, req2);

        // Second encoding should be shorter (indexed reference from dynamic table)
        Assert.True(block2.Length < block1.Length,
            "Second encoding of non-sensitive header should be smaller due to HPACK dynamic table caching");
    }

    [Fact(DisplayName = "7541-7.1.3-s016: Authorization never appears as indexed reference across repeated encodings")]
    public void Should_NeverUseIndexedReference_When_AuthorizationEncodedRepeatedly()
    {
        // If authorization were cached (incorrectly), the 2nd and 3rd HPACK-only encoding would shrink.
        // Test at HpackEncoder level to eliminate pseudo-header noise.
        var encoder = new HpackEncoder(useHuffman: false);
        var headers = new List<(string Name, string Value)>
        {
            ("authorization", "Bearer repeated-token")
        };

        var size1 = encoder.Encode(headers).Length;
        var size2 = encoder.Encode(headers).Length;
        var size3 = encoder.Encode(headers).Length;

        // All three must be the same size — no caching of NeverIndexed headers
        Assert.Equal(size1, size2);
        Assert.Equal(size2, size3);
        // Additionally verify each encoding is decoded as NeverIndexed
        var decoded = new HpackDecoder().Decode(encoder.Encode(headers).Span);
        Assert.True(decoded.First(h => h.Name == "authorization").NeverIndex);
    }

    [Fact(DisplayName = "7541-7.1.3-s017: Cookie never appears as indexed reference across repeated encodings")]
    public void Should_NeverUseIndexedReference_When_CookieEncodedRepeatedly()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var headers = new List<(string Name, string Value)>
        {
            ("cookie", "session=stable-value")
        };

        var size1 = encoder.Encode(headers).Length;
        var size2 = encoder.Encode(headers).Length;
        var size3 = encoder.Encode(headers).Length;

        Assert.Equal(size1, size2);
        Assert.Equal(size2, size3);
        var decoded = new HpackDecoder().Decode(encoder.Encode(headers).Span);
        Assert.True(decoded.First(h => h.Name == "cookie").NeverIndex);
    }

    // =========================================================================
    // Category 4: Round-Trip Value Correctness (6 tests)
    // =========================================================================

    [Fact(DisplayName = "7541-7.1.3-s018: Authorization value preserved through encode/decode round-trip")]
    public void Should_PreserveValue_When_AuthorizationRoundTrip()
    {
        const string token = "Bearer eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9.payload.signature";
        var request = MakeGetRequest();
        request.Headers.TryAddWithoutValidation("Authorization", token);

        var decoded = EncodeAndDecodeHeaders(request);
        var header = decoded.First(h => h.Name == "authorization");

        Assert.Equal(token, header.Value);
        Assert.True(header.NeverIndex);
    }

    [Fact(DisplayName = "7541-7.1.3-s019: Proxy-Authorization value preserved through encode/decode round-trip")]
    public void Should_PreserveValue_When_ProxyAuthorizationRoundTrip()
    {
        const string value = "Basic dXNlcjpwYXNzd29yZA==";
        var request = MakeGetRequest();
        request.Headers.TryAddWithoutValidation("Proxy-Authorization", value);

        var decoded = EncodeAndDecodeHeaders(request);
        var header = decoded.First(h => h.Name == "proxy-authorization");

        Assert.Equal(value, header.Value);
        Assert.True(header.NeverIndex);
    }

    [Fact(DisplayName = "7541-7.1.3-s020: Cookie value preserved through encode/decode round-trip")]
    public void Should_PreserveValue_When_CookieRoundTrip()
    {
        const string value = "session=abc123; userId=42; csrfToken=xyz789";
        var request = MakeGetRequest();
        request.Headers.TryAddWithoutValidation("Cookie", value);

        var decoded = EncodeAndDecodeHeaders(request);
        var header = decoded.First(h => h.Name == "cookie");

        Assert.Equal(value, header.Value);
        Assert.True(header.NeverIndex);
    }

    [Fact(DisplayName = "7541-7.1.3-s021: Set-Cookie value preserved through encode/decode round-trip")]
    public void Should_PreserveValue_When_SetCookieRoundTrip()
    {
        const string value = "sessionId=38afes71g; HttpOnly; Secure; SameSite=Strict";
        var request = MakeGetRequest();
        request.Headers.TryAddWithoutValidation("Set-Cookie", value);

        var decoded = EncodeAndDecodeHeaders(request);
        var header = decoded.First(h => h.Name == "set-cookie");

        Assert.Equal(value, header.Value);
        Assert.True(header.NeverIndex);
    }

    [Fact(DisplayName = "7541-7.1.3-s022: Mixed request: sensitive and non-sensitive headers both encoded correctly")]
    public void Should_EncodeBothCorrectly_When_MixedSensitiveAndNonSensitiveHeaders()
    {
        var request = MakeGetRequest();
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer mixed-test-token");
        request.Headers.TryAddWithoutValidation("X-Request-Id", "req-001");
        request.Headers.Accept.ParseAdd("application/json");

        var decoded = EncodeAndDecodeHeaders(request);

        var auth = decoded.First(h => h.Name == "authorization");
        var reqId = decoded.First(h => h.Name == "x-request-id");
        var accept = decoded.First(h => h.Name == "accept");

        Assert.True(auth.NeverIndex, "Authorization should be NeverIndexed");
        Assert.Equal("Bearer mixed-test-token", auth.Value);
        Assert.False(reqId.NeverIndex, "X-Request-Id should not be NeverIndexed");
        Assert.False(accept.NeverIndex, "Accept should not be NeverIndexed");
    }

    [Fact(DisplayName = "7541-7.1.3-s023: Multiple sensitive headers in one request are all NeverIndexed")]
    public void Should_EncodeAllAsNeverIndexed_When_MultipleSensitiveHeaders()
    {
        var request = MakeGetRequest();
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer token");
        request.Headers.TryAddWithoutValidation("Cookie", "session=abc");
        request.Headers.TryAddWithoutValidation("Proxy-Authorization", "Basic dXNlcjpwYXNz");

        var decoded = EncodeAndDecodeHeaders(request);

        var sensitiveHeaders = decoded
            .Where(h => h.Name is "authorization" or "cookie" or "proxy-authorization")
            .ToList();

        Assert.Equal(3, sensitiveHeaders.Count);
        Assert.True(sensitiveHeaders.All(h => h.NeverIndex),
            "All sensitive headers must be NeverIndexed per RFC 7541 §7.1.3");
    }

    // =========================================================================
    // Category 5: HpackEncoder Direct API Tests (4 tests)
    // =========================================================================

    [Fact(DisplayName = "7541-7.1.3-s024: HpackHeader with NeverIndex=true is encoded as NeverIndexed")]
    public void Should_EncodeAsNeverIndexed_When_HpackHeaderNeverIndexTrue()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var headers = new List<HpackHeader>
        {
            new("x-secret", "sensitive-value", NeverIndex: true)
        };

        var output = new ArrayBufferWriter<byte>(256);
        encoder.Encode(headers, output, useHuffman: false);

        var decoded = new HpackDecoder().Decode(output.WrittenSpan);
        var header = decoded.First(h => h.Name == "x-secret");

        Assert.True(header.NeverIndex, "HpackHeader with NeverIndex=true must produce NeverIndexed wire encoding");
    }

    [Fact(DisplayName = "7541-7.1.3-s025: HpackHeader with NeverIndex=false for non-sensitive name uses IncrementalIndexing")]
    public void Should_UseIncrementalIndexing_When_HpackHeaderNeverIndexFalse()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var headers = new List<HpackHeader>
        {
            new("x-custom", "some-value", NeverIndex: false)
        };

        var output = new ArrayBufferWriter<byte>(256);
        encoder.Encode(headers, output, useHuffman: false);

        var decoded = new HpackDecoder().Decode(output.WrittenSpan);
        var header = decoded.First(h => h.Name == "x-custom");

        Assert.False(header.NeverIndex);
    }

    [Fact(DisplayName = "7541-7.1.3-s026: Sensitive header name auto-upgraded to NeverIndexed even if NeverIndex=false")]
    public void Should_AutoUpgradeToNeverIndexed_When_SensitiveNameRegardlessOfFlag()
    {
        // Per RFC 7541 §7.1: the encoder MUST use NeverIndexed for sensitive headers
        // regardless of what the caller specified.
        var encoder = new HpackEncoder(useHuffman: false);
        var headers = new List<HpackHeader>
        {
            new("authorization", "Bearer token", NeverIndex: false)
        };

        var output = new ArrayBufferWriter<byte>(256);
        encoder.Encode(headers, output, useHuffman: false);

        var decoded = new HpackDecoder().Decode(output.WrittenSpan);
        var header = decoded.First(h => h.Name == "authorization");

        Assert.True(header.NeverIndex,
            "Authorization must be NeverIndexed even when HpackHeader.NeverIndex=false (auto-upgrade per RFC 7541 §7.1)");
    }

    [Fact(DisplayName = "7541-7.1.3-s027: NeverIndexed header not added to dynamic table (two encodings same size)")]
    public void Should_NotAddToDynamicTable_When_NeverIndexedHeaderEncoded()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var headersList = new List<HpackHeader> { new("authorization", "Bearer token") };

        var output1 = new ArrayBufferWriter<byte>(256);
        encoder.Encode(headersList, output1, useHuffman: false);

        var output2 = new ArrayBufferWriter<byte>(256);
        encoder.Encode(headersList, output2, useHuffman: false);

        // NeverIndexed headers are never added to the dynamic table, so
        // encoding the same header twice must produce identical byte counts.
        Assert.Equal(output1.WrittenCount, output2.WrittenCount);
    }

    // =========================================================================
    // Category 6: Http2Encoder Full Frame Integration Tests (5 tests)
    // =========================================================================

    [Fact(DisplayName = "7541-7.1.3-s028: Full HTTP/2 GET frame with Authorization: decoded NeverIndex=true")]
    public void Should_ProduceNeverIndexedFrame_When_Http2GetRequestWithAuthorization()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/data");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer access-token");

        var decoded = EncodeAndDecodeHeaders(request, useHuffman: false);
        var auth = decoded.First(h => h.Name == "authorization");

        Assert.True(auth.NeverIndex);
        Assert.Equal("Bearer access-token", auth.Value);
    }

    [Fact(DisplayName = "7541-7.1.3-s029: Full HTTP/2 POST frame with Authorization and body: NeverIndexed preserved")]
    public void Should_PreserveSensitiveHeader_When_PostRequestWithBodyAndAuthorization()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.example.com/users");
        request.Content = new StringContent("{\"name\":\"Alice\"}", Encoding.UTF8, "application/json");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer post-token");

        var decoded = EncodeAndDecodeHeaders(request, useHuffman: false);
        var auth = decoded.First(h => h.Name == "authorization");

        Assert.True(auth.NeverIndex);
        Assert.Equal("Bearer post-token", auth.Value);
    }

    [Fact(DisplayName = "7541-7.1.3-s030: Request without sensitive headers has no NeverIndexed entries")]
    public void Should_HaveNoNeverIndexedHeaders_When_NoSensitiveHeaders()
    {
        var request = MakeGetRequest();
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.TryAddWithoutValidation("X-Request-Id", "12345");

        var decoded = EncodeAndDecodeHeaders(request);
        var neverIndexed = decoded.Where(h => h.NeverIndex).ToList();

        Assert.Empty(neverIndexed);
    }

    [Fact(DisplayName = "7541-7.1.3-s031: Huffman encoding preserves NeverIndexed flag for Authorization")]
    public void Should_PreserveNeverIndexed_When_HuffmanEncodingEnabled()
    {
        var request = MakeGetRequest();
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer huffman-test");

        var decoded = EncodeAndDecodeHeaders(request, useHuffman: true);
        var auth = decoded.First(h => h.Name == "authorization");

        Assert.True(auth.NeverIndex, "NeverIndexed flag must be preserved with Huffman encoding");
        Assert.Equal("Bearer huffman-test", auth.Value);
    }

    [Fact(DisplayName = "7541-7.1.3-s032: All four sensitive header types NeverIndexed in single request")]
    public void Should_EncodeAllFourSensitiveHeaderTypes_When_AllPresent()
    {
        var request = MakeGetRequest();
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer token");
        request.Headers.TryAddWithoutValidation("Proxy-Authorization", "Basic dXNlcjpwYXNz");
        request.Headers.TryAddWithoutValidation("Cookie", "session=abc");
        request.Headers.TryAddWithoutValidation("Set-Cookie", "id=123; HttpOnly");

        var decoded = EncodeAndDecodeHeaders(request);

        foreach (var name in new[] { "authorization", "proxy-authorization", "cookie", "set-cookie" })
        {
            var header = decoded.FirstOrDefault(h => h.Name == name);
            Assert.NotNull(header.Name);
            Assert.True(header.NeverIndex,
                $"RFC 7541 §7.1.3: {name} must be encoded as NeverIndexed");
        }
    }

    // =========================================================================
    // Category 7: Edge Cases and Raw Byte Verification (3 tests)
    // =========================================================================

    [Fact(DisplayName = "7541-7.1.3-s033: Authorization raw HPACK bytes use NeverIndexed encoding (walker verified)")]
    public void Should_HaveNeverIndexedEncoding_When_AuthorizationEncodedRaw()
    {
        // Low-level verification via a proper HPACK byte walker.
        // authorization is at static index 23, so the NeverIndexed encoding uses the index,
        // not a literal name. The walker handles this correctly.
        var encoder = new Http2Encoder(useHuffman: false);
        var req = MakeGetRequest();
        req.Headers.TryAddWithoutValidation("Authorization", "Bearer raw-check");
        var block = ExtractHpackBlockFromEncoder(encoder, req);

        Assert.True(IsHeaderEncodedAsNeverIndexed(block, "authorization"),
            "The HPACK byte stream must use NeverIndexed encoding for authorization (RFC 7541 §6.2.3)");
    }

    [Fact(DisplayName = "7541-7.1.3-s034: Cookie raw HPACK bytes use NeverIndexed encoding (walker verified)")]
    public void Should_HaveNeverIndexedEncoding_When_CookieEncodedRaw()
    {
        var encoder = new Http2Encoder(useHuffman: false);
        var req = MakeGetRequest();
        req.Headers.TryAddWithoutValidation("Cookie", "session=walker-check");
        var block = ExtractHpackBlockFromEncoder(encoder, req);

        Assert.True(IsHeaderEncodedAsNeverIndexed(block, "cookie"),
            "The HPACK byte stream must use NeverIndexed encoding for cookie (RFC 7541 §6.2.3)");
    }

    [Fact(DisplayName = "7541-7.1.3-s035: Non-sensitive header raw HPACK bytes use IncrementalIndexing (walker verified)")]
    public void Should_HaveIncrementalIndexingEncoding_When_NonSensitiveHeaderEncodedRaw()
    {
        var encoder = new Http2Encoder(useHuffman: false);
        var req = MakeGetRequest();
        req.Headers.TryAddWithoutValidation("X-Correlation-Id", "corr-abc123");
        var block = ExtractHpackBlockFromEncoder(encoder, req);

        // Non-sensitive headers use IncrementalIndexing, not NeverIndexed
        Assert.False(IsHeaderEncodedAsNeverIndexed(block, "x-correlation-id"),
            "Non-sensitive header should NOT use the NeverIndexed encoding");
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static HttpRequestMessage MakeGetRequest(string url = "https://api.example.com/v1/resource")
        => new(HttpMethod.Get, url);

    private static List<HpackHeader> EncodeAndDecodeHeaders(HttpRequestMessage request, bool useHuffman = false)
    {
        var encoder = new Http2Encoder(useHuffman);
        using var owner = MemoryPool<byte>.Shared.Rent(16384);
        var buffer = owner.Memory;
        var (_, written) = encoder.Encode(request, ref buffer);
        var hpackBlock = ExtractHpackBlock(buffer.Span[..written]);
        return new HpackDecoder().Decode(hpackBlock);
    }

    private static byte[] ExtractHpackBlockFromEncoder(Http2Encoder encoder, HttpRequestMessage request)
    {
        using var owner = MemoryPool<byte>.Shared.Rent(16384);
        var buffer = owner.Memory;
        var (_, written) = encoder.Encode(request, ref buffer);
        return ExtractHpackBlock(buffer.Span[..written]);
    }

    private static byte[] ExtractHpackBlock(ReadOnlySpan<byte> frameData)
    {
        var payloadLen = (frameData[0] << 16) | (frameData[1] << 8) | frameData[2];
        return frameData[9..(9 + payloadLen)].ToArray();
    }

    /// <summary>
    /// Walks the raw HPACK byte stream and returns true if the header with the given name
    /// is encoded as NeverIndexed (RFC 7541 §6.2.3, bit pattern 0001xxxx).
    ///
    /// Handles all cases:
    ///   - Header name from static table (nameIndex > 0) — e.g., authorization is static index 23
    ///   - Header name as literal (nameIndex == 0)
    ///   - Single-byte and multi-byte HPACK integer encodings
    ///
    /// Only valid for non-Huffman encoded HPACK blocks (H-bit = 0 on strings).
    /// </summary>
    private static bool IsHeaderEncodedAsNeverIndexed(byte[] hpackBlock, string targetName)
    {
        var span = hpackBlock.AsSpan();
        var pos = 0;

        while (pos < span.Length)
        {
            var b = span[pos];

            if ((b & 0x80) != 0)
            {
                // §6.1 Indexed Header Field — not a literal, skip
                ReadHpackInt(span, ref pos, 7);
                continue;
            }

            if ((b & 0xE0) == 0x20)
            {
                // §6.3 Dynamic Table Size Update — skip
                ReadHpackInt(span, ref pos, 5);
                continue;
            }

            bool isNeverIndexed;
            int prefixBits;

            if ((b & 0xC0) == 0x40)
            {
                // §6.2.1 Literal with Incremental Indexing
                isNeverIndexed = false;
                prefixBits = 6;
            }
            else if ((b & 0x10) != 0)
            {
                // §6.2.3 Literal Never Indexed
                isNeverIndexed = true;
                prefixBits = 4;
            }
            else
            {
                // §6.2.2 Literal without Indexing
                isNeverIndexed = false;
                prefixBits = 4;
            }

            var nameIdx = ReadHpackInt(span, ref pos, prefixBits);

            string name;
            if (nameIdx == 0)
            {
                // Literal name string follows
                name = ReadHpackStringRaw(span, ref pos);
            }
            else
            {
                // Name from static or dynamic table
                name = nameIdx <= HpackStaticTable.StaticCount
                    ? HpackStaticTable.Entries[nameIdx].Name
                    : "<dynamic>";
            }

            // Skip the value string
            ReadHpackStringRaw(span, ref pos);

            if (string.Equals(name, targetName, StringComparison.OrdinalIgnoreCase))
            {
                return isNeverIndexed;
            }
        }

        return false; // header not found in block
    }

    /// <summary>Reads an HPACK integer (RFC 7541 §5.1) and advances pos.</summary>
    private static int ReadHpackInt(ReadOnlySpan<byte> data, ref int pos, int prefixBits)
    {
        var mask = (1 << prefixBits) - 1;
        var value = data[pos] & mask;
        pos++;

        if (value < mask)
        {
            return value;
        }

        var shift = 0;
        while (pos < data.Length)
        {
            var cont = data[pos++];
            value += (cont & 0x7F) << shift;
            shift += 7;
            if ((cont & 0x80) == 0)
            {
                break;
            }
        }

        return value;
    }

    /// <summary>
    /// Reads an HPACK string literal (RFC 7541 §5.2) without Huffman decoding
    /// (assumes H-bit = 0, i.e., raw ASCII). Advances pos past the string.
    /// </summary>
    private static string ReadHpackStringRaw(ReadOnlySpan<byte> data, ref int pos)
    {
        var len = ReadHpackInt(data, ref pos, 7); // H-bit is 7th bit; value = lower 7 bits
        var str = Encoding.UTF8.GetString(data.Slice(pos, len));
        pos += len;
        return str;
    }
}
