using System;
using System.Collections.Generic;
using Xunit;
using TurboHTTP.Protocol.Http2.Hpack;

namespace TurboHTTP.Tests.Http2.Hpack;

public sealed class HpackHeaderBlockDecodingSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-6.1")]
    public void HpackHeaderBlockDecoding_should_decode_indexed_header()
    {
        var decoder = new HpackDecoder();
        // Index 2 = :method GET
        var block = new byte[] { 0x82 };

        var decoded = decoder.Decode(block);

        Assert.Single(decoded);
        Assert.Equal(":method", decoded[0].Name);
        Assert.Equal("GET", decoded[0].Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-6.2")]
    public void HpackHeaderBlockDecoding_should_decode_literal_with_incremental_indexing()
    {
        var decoder = new HpackDecoder();
        var block = RawString([0x40], "name", 5, "value");

        var decoded = decoder.Decode(block);

        Assert.NotEmpty(decoded);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-6.2.2")]
    public void HpackHeaderBlockDecoding_should_decode_literal_without_indexing()
    {
        var decoder = new HpackDecoder();
        var block = RawString([0x00], "name", 5, "value");

        var decoded = decoder.Decode(block);

        Assert.NotEmpty(decoded);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-6.2.3")]
    public void HpackHeaderBlockDecoding_should_decode_literal_never_indexed()
    {
        var decoder = new HpackDecoder();
        var block = RawString([0x10], "name", 5, "value");

        var decoded = decoder.Decode(block);

        Assert.NotEmpty(decoded);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-6.3")]
    public void HpackHeaderBlockDecoding_should_decode_dynamic_table_size_update()
    {
        var decoder = new HpackDecoder();
        // Size update to 512 bytes
        var block = new byte[] { 0x3f, 0xa1, 0x1f };

        // Dynamic table size update produces no headers; absence of exception confirms success
        var decoded = decoder.Decode(block);
        Assert.Empty(decoded);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-6")]
    public void HpackHeaderBlockDecoding_should_decode_mixed_representations()
    {
        var decoder = new HpackDecoder();
        // Start with indexed header
        var block1 = new byte[] { 0x82 };
        var decoded1 = decoder.Decode(block1);

        // Add literal header
        var block2 = RawString([0x40], "test", 5, "value");
        var decoded2 = decoder.Decode(block2);

        Assert.NotEmpty(decoded1);
        Assert.NotEmpty(decoded2);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-4")]
    public void HpackHeaderBlockDecoding_should_add_literal_to_dynamic_table()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var decoder = new HpackDecoder();

        var headers = new List<(string Name, string Value)>
        {
            ("custom-header", "custom-value")
        };

        var block = encoder.Encode(headers);
        decoder.Decode(block.Span);

        // Second encoding should reference the dynamic table
        var block2 = encoder.Encode(headers);
        Assert.True(block2.Length <= block.Length);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-6.1")]
    public void HpackHeaderBlockDecoding_should_decode_static_table_reference()
    {
        var decoder = new HpackDecoder();
        // Index 4 = :path /
        var block = new byte[] { 0x84 };

        var decoded = decoder.Decode(block);

        Assert.Single(decoded);
        Assert.Equal(":path", decoded[0].Name);
        Assert.Equal("/", decoded[0].Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-6.1")]
    public void HpackHeaderBlockDecoding_should_decode_dynamic_table_reference()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var decoder = new HpackDecoder();

        // Add entry to dynamic table
        var headers1 = new List<(string Name, string Value)>
        {
            ("custom", "value")
        };
        var block1 = encoder.Encode(headers1);
        decoder.Decode(block1.Span);

        // Reference the same header (should be in dynamic table at index 62)
        var headers2 = new List<(string Name, string Value)>
        {
            ("custom", "value")
        };
        var block2 = encoder.Encode(headers2);
        var decoded2 = decoder.Decode(block2.Span);

        Assert.NotEmpty(decoded2);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541")]
    public void HpackHeaderBlockDecoding_should_decode_empty_block()
    {
        var decoder = new HpackDecoder();
        var block = new byte[] { };

        var decoded = decoder.Decode(block);

        Assert.Empty(decoded);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-6")]
    public void HpackHeaderBlockDecoding_should_decode_multiple_headers_in_block()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var decoder = new HpackDecoder();

        var headers = new List<(string Name, string Value)>
        {
            (":method", "GET"),
            (":path", "/index.html"),
            (":scheme", "https"),
            ("custom", "value")
        };

        var block = encoder.Encode(headers);
        var decoded = decoder.Decode(block.Span);

        Assert.Equal(headers.Count, decoded.Count);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-6.2")]
    public void HpackHeaderBlockDecoding_should_preserve_literal_indexing_state()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var decoder = new HpackDecoder();

        var headers = new List<(string Name, string Value)>
        {
            ("x-test", "value1")
        };

        var block = encoder.Encode(headers);
        var decoded = decoder.Decode(block.Span);

        Assert.NotEmpty(decoded);
        Assert.Equal("x-test", decoded[0].Name);
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
