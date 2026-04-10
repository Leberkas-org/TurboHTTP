using System;
using System.Collections.Generic;
using System.Text;
using Xunit;
using TurboHTTP.Protocol.Http2.Hpack;
using TurboHTTP.Protocol.Http2;

namespace TurboHTTP.Tests.Http2.Hpack;

public sealed class HpackSensitiveHeaderVerificationSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-7.1.3")]
    public void HpackSensitiveHeaderVerification_should_encode_mixed_sensitive_and_non_sensitive_correctly()
    {
        var request = MakeGetRequest();
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer mixed-test-token");
        request.Headers.TryAddWithoutValidation("X-Request-Id", "req-001");
        request.Headers.Accept.ParseAdd("application/json");

        var decoded = EncodeAndDecodeHeaders(request);

        var auth = decoded.First(h => h.Name == "authorization");
        var reqId = decoded.First(h => h.Name == "x-request-id");
        var accept = decoded.First(h => h.Name == "accept");

        Assert.True(auth.NeverIndex);
        Assert.Equal("Bearer mixed-test-token", auth.Value);
        Assert.False(reqId.NeverIndex);
        Assert.False(accept.NeverIndex);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-7.1.3")]
    public void HpackSensitiveHeaderVerification_should_encode_all_multiple_sensitive_as_never_indexed()
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

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-7.1.3")]
    public void HpackSensitiveHeaderVerification_should_encode_hpack_header_with_never_index_true()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var headers = new List<HpackHeader>
        {
            new("x-secret", "sensitive-value", NeverIndex: true)
        };

        var output = new byte[256];
        Span<byte> span = output;
        var written = encoder.Encode(headers, ref span, useHuffman: false);

        var decoded = new HpackDecoder().Decode(output.AsSpan(0, written));
        var header = decoded.First(h => h.Name == "x-secret");

        Assert.True(header.NeverIndex);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-7.1.3")]
    public void HpackSensitiveHeaderVerification_should_use_incremental_indexing_when_never_index_false()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var headers = new List<HpackHeader>
        {
            new("x-custom", "some-value", NeverIndex: false)
        };

        var output = new byte[256];
        Span<byte> span = output;
        var written = encoder.Encode(headers, ref span, useHuffman: false);

        var decoded = new HpackDecoder().Decode(output.AsSpan(0, written));
        var header = decoded.First(h => h.Name == "x-custom");

        Assert.False(header.NeverIndex);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-7.1.3")]
    public void HpackSensitiveHeaderVerification_should_auto_upgrade_sensitive_to_never_indexed()
    {
        // Per RFC 7541 §7.1: the encoder MUST use NeverIndexed for sensitive headers
        // regardless of what the caller specified.
        var encoder = new HpackEncoder(useHuffman: false);
        var headers = new List<HpackHeader>
        {
            new("authorization", "Bearer token", NeverIndex: false)
        };

        var output = new byte[256];
        Span<byte> span = output;
        var written = encoder.Encode(headers, ref span, useHuffman: false);

        var decoded = new HpackDecoder().Decode(output.AsSpan(0, written));
        var header = decoded.First(h => h.Name == "authorization");

        Assert.True(header.NeverIndex,
            "Authorization must be NeverIndexed even when HpackHeader.NeverIndex=false (auto-upgrade per RFC 7541 §7.1)");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-7.1.3")]
    public void HpackSensitiveHeaderVerification_should_not_add_never_indexed_to_dynamic_table()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var headersList = new List<HpackHeader> { new("authorization", "Bearer token") };

        var output1 = new byte[256];
        Span<byte> span1 = output1;
        var written1 = encoder.Encode(headersList, ref span1, useHuffman: false);

        var output2 = new byte[256];
        Span<byte> span2 = output2;
        var written2 = encoder.Encode(headersList, ref span2, useHuffman: false);

        // NeverIndexed headers are never added to the dynamic table, so
        // encoding the same header twice must produce identical byte counts.
        Assert.Equal(written1, written2);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-7.1.3")]
    public void HpackSensitiveHeaderVerification_should_produce_never_indexed_frame_for_get_with_authorization()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/data");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer access-token");

        var decoded = EncodeAndDecodeHeaders(request, useHuffman: false);
        var auth = decoded.First(h => h.Name == "authorization");

        Assert.True(auth.NeverIndex);
        Assert.Equal("Bearer access-token", auth.Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-7.1.3")]
    public void HpackSensitiveHeaderVerification_should_preserve_sensitive_in_post_with_body_and_authorization()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.example.com/users");
        request.Content = new StringContent("{\"name\":\"Alice\"}", Encoding.UTF8, "application/json");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer post-token");

        var decoded = EncodeAndDecodeHeaders(request, useHuffman: false);
        var auth = decoded.First(h => h.Name == "authorization");

        Assert.True(auth.NeverIndex);
        Assert.Equal("Bearer post-token", auth.Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-7.1.3")]
    public void HpackSensitiveHeaderVerification_should_have_no_never_indexed_without_sensitive_headers()
    {
        var request = MakeGetRequest();
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.TryAddWithoutValidation("X-Request-Id", "12345");

        var decoded = EncodeAndDecodeHeaders(request);
        var neverIndexed = decoded.Where(h => h.NeverIndex).ToList();

        Assert.Empty(neverIndexed);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-7.1.3")]
    public void HpackSensitiveHeaderVerification_should_preserve_never_indexed_with_huffman_encoding()
    {
        var request = MakeGetRequest();
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer huffman-test");

        var decoded = EncodeAndDecodeHeaders(request, useHuffman: true);
        var auth = decoded.First(h => h.Name == "authorization");

        Assert.True(auth.NeverIndex);
        Assert.Equal("Bearer huffman-test", auth.Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-7.1.3")]
    public void HpackSensitiveHeaderVerification_should_encode_all_four_sensitive_types()
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

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-7.1.3")]
    public void HpackSensitiveHeaderVerification_should_have_never_indexed_encoding_for_authorization()
    {
        // Low-level verification via a proper HPACK byte walker.
        // authorization is at static index 23, so the NeverIndexed encoding uses the index,
        // not a literal name. The walker handles this correctly.
        var encoder = new RequestEncoder(useHuffman: false);
        var req = MakeGetRequest();
        req.Headers.TryAddWithoutValidation("Authorization", "Bearer raw-check");
        var block = ExtractHpackBlockFromEncoder(encoder, req);

        Assert.True(IsHeaderEncodedAsNeverIndexed(block, "authorization"),
            "The HPACK byte stream must use NeverIndexed encoding for authorization (RFC 7541 §6.2.3)");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-7.1.3")]
    public void HpackSensitiveHeaderVerification_should_have_never_indexed_encoding_for_cookie()
    {
        var encoder = new RequestEncoder(useHuffman: false);
        var req = MakeGetRequest();
        req.Headers.TryAddWithoutValidation("Cookie", "session=walker-check");
        var block = ExtractHpackBlockFromEncoder(encoder, req);

        Assert.True(IsHeaderEncodedAsNeverIndexed(block, "cookie"),
            "The HPACK byte stream must use NeverIndexed encoding for cookie (RFC 7541 §6.2.3)");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-7.1.3")]
    public void HpackSensitiveHeaderVerification_should_have_incremental_indexing_for_non_sensitive()
    {
        var encoder = new RequestEncoder(useHuffman: false);
        var req = MakeGetRequest();
        req.Headers.TryAddWithoutValidation("X-Correlation-Id", "corr-abc123");
        var block = ExtractHpackBlockFromEncoder(encoder, req);

        // Non-sensitive headers use IncrementalIndexing, not NeverIndexed
        Assert.False(IsHeaderEncodedAsNeverIndexed(block, "x-correlation-id"),
            "Non-sensitive header should NOT use the NeverIndexed encoding");
    }

    private static HttpRequestMessage MakeGetRequest(string url = "https://api.example.com/v1/resource")
        => new(HttpMethod.Get, url);

    private static List<HpackHeader> EncodeAndDecodeHeaders(HttpRequestMessage request, bool useHuffman = false)
    {
        var encoder = new RequestEncoder(useHuffman);
        var hpackBlock = encoder.EncodeToHpackBlock(request);
        return new HpackDecoder().Decode(hpackBlock);
    }

    private static byte[] ExtractHpackBlockFromEncoder(RequestEncoder encoder, HttpRequestMessage request)
    {
        return encoder.EncodeToHpackBlock(request);
    }

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

    private static string ReadHpackStringRaw(ReadOnlySpan<byte> data, ref int pos)
    {
        var len = ReadHpackInt(data, ref pos, 7); // H-bit is 7th bit; value = lower 7 bits
        var str = Encoding.UTF8.GetString(data.Slice(pos, len));
        pos += len;
        return str;
    }
}
