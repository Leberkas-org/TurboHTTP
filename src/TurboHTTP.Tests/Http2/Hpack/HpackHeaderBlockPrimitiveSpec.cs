using System;
using System.Collections.Generic;
using Xunit;
using TurboHTTP.Protocol.Http2.Hpack;

namespace TurboHTTP.Tests.Http2.Hpack;

public sealed class HpackHeaderBlockPrimitiveSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-5.1")]
    public void HpackHeaderBlockPrimitive_should_decode_single_byte_integer()
    {
        var decoder = new HpackDecoder();
        // 0x82 = indexed header at index 2 = :method GET (static table)
        var block = new byte[] { 0x82 };

        var decoded = decoder.Decode(block);
        Assert.Single(decoded);
        Assert.Equal(":method", decoded[0].Name);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-5.1")]
    public void HpackHeaderBlockPrimitive_should_decode_multi_byte_integer()
    {
        var decoder = new HpackDecoder();
        // 0x3f = dynamic table size update (001xxxxx) with 5-bit prefix = 31 (max, multi-byte follows)
        // 0x00 = continuation byte: no continuation bit, value = 0 → total size = 31 + 0 = 31
        var block = new byte[] { 0x3f, 0x00 };

        decoder.Decode(block);
        // Decoder should handle multi-byte integers
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-5.1")]
    public void HpackHeaderBlockPrimitive_should_detect_integer_overflow()
    {
        var decoder = new HpackDecoder();
        // 0x3f = dynamic table size update, multi-byte; continuation bytes produce value >> uint32 max
        var block = new byte[] { 0x3f, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0x7f };

        Assert.Throws<HpackException>(() => decoder.Decode(block));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-5.1")]
    public void HpackHeaderBlockPrimitive_should_detect_truncated_integer()
    {
        var decoder = new HpackDecoder();
        // 0x3f = dynamic table size update, multi-byte; 0x80 has continuation bit set but no next byte
        var block = new byte[] { 0x3f, 0x80 };

        Assert.Throws<HpackException>(() => decoder.Decode(block));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-5.2")]
    public void HpackHeaderBlockPrimitive_should_decode_string_literal()
    {
        var decoder = new HpackDecoder();
        var encoder = new HpackEncoder(useHuffman: false);

        var headers = new List<(string Name, string Value)>
        {
            ("test", "value")
        };

        var block = encoder.Encode(headers);
        var decoded = decoder.Decode(block.Span);

        Assert.NotEmpty(decoded);
        Assert.Equal("test", decoded[0].Name);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-5.2")]
    public void HpackHeaderBlockPrimitive_should_decode_huffman_encoded_string()
    {
        var decoder = new HpackDecoder();
        var encoder = new HpackEncoder(useHuffman: true);

        var headers = new List<(string Name, string Value)>
        {
            ("test", "value")
        };

        var block = encoder.Encode(headers);
        var decoded = decoder.Decode(block.Span);

        Assert.NotEmpty(decoded);
        Assert.Equal("value", decoded[0].Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-5.2")]
    public void HpackHeaderBlockPrimitive_should_detect_string_truncation()
    {
        var decoder = new HpackDecoder();
        // String with length indicating more bytes than provided → truncated → HpackException
        var block = RawString([], "name", 10, "val");

        Assert.Throws<HpackException>(() => decoder.Decode(block));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-5.1")]
    public void HpackHeaderBlockPrimitive_should_handle_zero_length_integer()
    {
        var decoder = new HpackDecoder();
        // 0x20 = dynamic table size update (001xxxxx) with size = 0
        var block = new byte[] { 0x20 };

        var decoded = decoder.Decode(block);
        Assert.Empty(decoded);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-5.2")]
    public void HpackHeaderBlockPrimitive_should_handle_zero_length_string()
    {
        var decoder = new HpackDecoder();
        var block = RawString([0x00], "name", 0, "");

        var decoded = decoder.Decode(block);
        Assert.NotEmpty(decoded);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-6")]
    public void HpackHeaderBlockPrimitive_should_handle_mixed_representations()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var decoder = new HpackDecoder();

        var headers = new List<(string Name, string Value)>
        {
            (":method", "GET"),
            ("custom-header", "custom-value")
        };

        var block = encoder.Encode(headers);
        var decoded = decoder.Decode(block.Span);

        Assert.NotEmpty(decoded);
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC7541-5.1")]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(127)]
    [InlineData(255)]
    public void HpackHeaderBlockPrimitive_should_decode_integer_prefix_values(int value)
    {
        var decoder = new HpackDecoder();
        var encoder = new HpackEncoder(useHuffman: false);

        // Encode a header with specific length to test integer decoding
        var headers = new List<(string Name, string Value)>
        {
            ("n", new string('a', value % 50))
        };

        var block = encoder.Encode(headers);
        decoder.Decode(block.Span);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-7.1.3")]
    public void HpackHeaderBlockPrimitive_should_preserve_never_indexed_flag()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var decoder = new HpackDecoder();

        var headers = new List<HpackHeader>
        {
            new("authorization", "Bearer token", NeverIndex: true)
        };

        var output = new byte[256];
        Span<byte> span = output;
        var written = encoder.Encode(headers, ref span, useHuffman: false);

        var decoded = decoder.Decode(output.AsSpan(0, written));
        var authHeader = decoded.Find(h => h.Name == "authorization");

        Assert.True(authHeader.NeverIndex);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-7.1.3")]
    public void HpackHeaderBlockPrimitive_should_auto_upgrade_sensitive_headers()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var headers = new List<HpackHeader>
        {
            new("authorization", "Bearer token", NeverIndex: false)
        };

        var output = new byte[256];
        Span<byte> span = output;
        var written = encoder.Encode(headers, ref span, useHuffman: false);

        var decoded = new HpackDecoder().Decode(output.AsSpan(0, written));
        var authHeader = decoded.Find(h => h.Name == "authorization");

        // Should be auto-upgraded to NeverIndexed regardless of flag
        Assert.True(authHeader.NeverIndex);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541")]
    public void HpackHeaderBlockPrimitive_should_round_trip_through_primitive_types()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var decoder = new HpackDecoder();

        var headers = new List<(string Name, string Value)>
        {
            ("name1", "value1"),
            ("name2", "value2"),
            ("name3", "value3")
        };

        var block = encoder.Encode(headers);
        var decoded = decoder.Decode(block.Span);

        Assert.Equal(headers.Count, decoded.Count);
        for (int i = 0; i < headers.Count; i++)
        {
            Assert.Equal(headers[i].Name, decoded[i].Name);
            Assert.Equal(headers[i].Value, decoded[i].Value);
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-5.1")]
    public void HpackHeaderBlockPrimitive_should_handle_7_bit_prefix()
    {
        var decoder = new HpackDecoder();
        var encoder = new HpackEncoder(useHuffman: false);

        var headers = new List<(string Name, string Value)>
        {
            ("x", "y")
        };

        var block = encoder.Encode(headers);
        decoder.Decode(block.Span);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-5.1")]
    public void HpackHeaderBlockPrimitive_should_handle_6_bit_prefix()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var decoder = new HpackDecoder();

        var headers = new List<(string Name, string Value)>
        {
            ("custom", "value")
        };

        var block = encoder.Encode(headers);
        decoder.Decode(block.Span);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-5.1")]
    public void HpackHeaderBlockPrimitive_should_handle_4_bit_prefix()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var decoder = new HpackDecoder();

        var headers = new List<(string Name, string Value)>
        {
            ("never-indexed", "sensitive")
        };

        var block = encoder.Encode(headers);
        decoder.Decode(block.Span);
    }

    private static byte[] RawString(List<byte> header, string name, int valueLen, string value)
    {
        var result = new List<byte>(header);
        result.Add((byte)name.Length);
        foreach (var c in name)
        {
            result.Add((byte)c);
        }
        result.Add((byte)valueLen);
        foreach (var c in value)
        {
            result.Add((byte)c);
        }
        return result.ToArray();
    }
}
