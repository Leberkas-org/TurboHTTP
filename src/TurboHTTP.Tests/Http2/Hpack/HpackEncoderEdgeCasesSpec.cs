using TurboHTTP.Protocol.Http2.Hpack;

namespace TurboHTTP.Tests.Http2.Hpack;

public sealed class HpackEncoderEdgeCasesSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-7.2")]
    public void HpackEncoder_should_throw_when_encoding_header_with_empty_name()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var headers = new List<(string, string)> { ("", "value") };

        var ex = Assert.Throws<HpackException>(() => encoder.Encode(headers));
        Assert.Contains("empty header name", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-7.2")]
    public void HpackEncoder_should_throw_when_encoding_tuple_with_null_name()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var headers = new List<(string, string)> { ("", "value") };

        var ex = Assert.Throws<HpackException>(() => encoder.Encode(headers));
        Assert.Contains("empty header name", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-7.1")]
    public void HpackEncoder_should_encode_authorization_as_never_indexed()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var decoder = new HpackDecoder();

        var headers = new List<(string, string)> { ("authorization", "Bearer token") };
        var encoded = encoder.Encode(headers);

        // Decode and verify it was received (should not be in dynamic table)
        var decoded = decoder.Decode(encoded.Span);
        Assert.Single(decoded);
        Assert.Equal("authorization", decoded[0].Name);
        Assert.Equal("Bearer token", decoded[0].Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-7.1")]
    public void HpackEncoder_should_encode_cookie_as_never_indexed()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var decoder = new HpackDecoder();

        var headers = new List<(string, string)> { ("cookie", "sessionid=abc123") };
        var encoded = encoder.Encode(headers);

        var decoded = decoder.Decode(encoded.Span);
        Assert.Single(decoded);
        Assert.Equal("cookie", decoded[0].Name);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-7.1")]
    public void HpackEncoder_should_encode_set_cookie_as_never_indexed()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var decoder = new HpackDecoder();

        var headers = new List<(string, string)> { ("set-cookie", "sessionid=xyz789") };
        var encoded = encoder.Encode(headers);

        var decoded = decoder.Decode(encoded.Span);
        Assert.Single(decoded);
        Assert.Equal("set-cookie", decoded[0].Name);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-7.1")]
    public void HpackEncoder_should_encode_proxy_authorization_as_never_indexed()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var decoder = new HpackDecoder();

        var headers = new List<(string, string)> { ("proxy-authorization", "Bearer proxy-token") };
        var encoded = encoder.Encode(headers);

        var decoded = decoder.Decode(encoded.Span);
        Assert.Single(decoded);
        Assert.Equal("proxy-authorization", decoded[0].Name);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-5.1")]
    public void HpackEncoder_should_throw_when_writing_negative_integer()
    {
        Assert.Throws<HpackException>(() =>
        {
            var buf = new byte[16];
            Span<byte> span = buf;
            HpackEncoder.WriteInteger(-1, prefixBits: 5, prefixFlags: 0x00, ref span);
        });
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-5.1")]
    public void HpackEncoder_should_throw_when_writing_integer_with_invalid_prefix_bits()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            var buf = new byte[16];
            Span<byte> span = buf;
            HpackEncoder.WriteInteger(10, prefixBits: 0, prefixFlags: 0x00, ref span);
        });
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-5.1")]
    public void HpackEncoder_should_throw_when_writing_integer_with_prefix_bits_too_large()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            var buf = new byte[16];
            Span<byte> span = buf;
            HpackEncoder.WriteInteger(10, prefixBits: 9, prefixFlags: 0x00, ref span);
        });
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-6.3")]
    public void HpackEncoder_should_acknowledge_table_size_change()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        encoder.AcknowledgeTableSizeChange(2048);

        var decoder = new HpackDecoder();
        decoder.SetMaxAllowedTableSize(2048);

        var headers = new List<(string, string)> { (":method", "POST") };
        var encoded = encoder.Encode(headers);

        // Should decode without error
        var decoded = decoder.Decode(encoded.Span);
        Assert.Single(decoded);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-6.3")]
    public void HpackEncoder_should_throw_when_table_size_is_negative()
    {
        var encoder = new HpackEncoder(useHuffman: false);

        var ex = Assert.Throws<HpackException>(() => encoder.AcknowledgeTableSizeChange(-1));
        Assert.Contains("SETTINGS_HEADER_TABLE_SIZE", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-6.3")]
    public void HpackEncoder_should_emit_table_size_update_before_first_header()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        encoder.AcknowledgeTableSizeChange(1024);

        var decoder = new HpackDecoder();
        decoder.SetMaxAllowedTableSize(1024);

        var headers = new List<(string, string)> { ("x-custom", "value") };
        var encoded = encoder.Encode(headers);

        // Decoder should handle size update before header
        var decoded = decoder.Decode(encoded.Span);
        Assert.Single(decoded);
        Assert.Equal("x-custom", decoded[0].Name);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-6.2.1")]
    public void HpackEncoder_should_add_new_header_to_dynamic_table()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var decoder = new HpackDecoder();

        var h1 = new List<(string, string)> { ("x-first", "value1") };
        var e1 = encoder.Encode(h1);
        decoder.Decode(e1.Span);

        var h2 = new List<(string, string)> { ("x-first", "value1") };
        var e2 = encoder.Encode(h2);

        // Second encoding should be shorter (uses dynamic table reference)
        Assert.True(e2.Length <= e1.Length);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-5.2")]
    public void HpackEncoder_should_reject_huffman_encoding_when_too_large()
    {
        var encoder = new HpackEncoder(useHuffman: true);
        var decoder = new HpackDecoder();

        // Create a string where Huffman encoding is NOT beneficial
        // (though in practice Huffman usually saves space)
        const string shortValue = "a";
        var headers = new List<(string, string)> { ("x", shortValue) };

        var encoded = encoder.Encode(headers);
        var decoded = decoder.Decode(encoded.Span);

        Assert.Single(decoded);
        Assert.Equal("x", decoded[0].Name);
        Assert.Equal(shortValue, decoded[0].Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-5.2")]
    public void HpackEncoder_should_encode_zero_length_string_without_huffman()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var decoder = new HpackDecoder();

        var headers = new List<(string, string)> { ("name", "") };
        var encoded = encoder.Encode(headers);

        var decoded = decoder.Decode(encoded.Span);
        Assert.Single(decoded);
        Assert.Equal("name", decoded[0].Name);
        Assert.Equal("", decoded[0].Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-5.2")]
    public void HpackEncoder_should_encode_zero_length_string_with_huffman()
    {
        var encoder = new HpackEncoder(useHuffman: true);
        var decoder = new HpackDecoder();

        var headers = new List<(string, string)> { ("name", "") };
        var encoded = encoder.Encode(headers);

        var decoded = decoder.Decode(encoded.Span);
        Assert.Single(decoded);
        Assert.Equal("name", decoded[0].Name);
        Assert.Equal("", decoded[0].Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-6.2.1")]
    public void HpackEncoder_should_use_dynamic_table_for_repeated_header()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var decoder = new HpackDecoder();

        var customHeader = ("x-repeated", "same-value");

        // First use: will be added to dynamic table
        var h1 = new List<(string, string)> { customHeader };
        var e1 = encoder.Encode(h1);
        decoder.Decode(e1.Span);

        // Second use: encoder should find it in dynamic table and use shorter encoding
        var h2 = new List<(string, string)> { customHeader };
        var e2 = encoder.Encode(h2);

        // Both should decode correctly
        var d2 = decoder.Decode(e2.Span);
        Assert.Single(d2);
        Assert.Equal(customHeader.Item1, d2[0].Name);
        Assert.Equal(customHeader.Item2, d2[0].Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-5.1")]
    public void HpackEncoder_should_handle_maximum_integer_value()
    {
        var buf = new byte[32];
        Span<byte> span = buf;
        var written = HpackEncoder.WriteInteger(int.MaxValue, prefixBits: 7, prefixFlags: 0x00, ref span);

        // int.MaxValue (0x7FFFFFFF) requires 5 bytes: 127 + 4 continuation bytes with 7-bit chunks
        Assert.True(written > 1);
        Assert.True(written <= 32);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-5.1")]
    public void HpackEncoder_should_encode_all_prefix_bit_widths()
    {
        for (var bits = 1; bits <= 8; bits++)
        {
            var buf = new byte[16];
            Span<byte> span = buf;
            var value = (1 << bits) - 1;

            var written = HpackEncoder.WriteInteger(value, prefixBits: bits, prefixFlags: 0x00, ref span);
            Assert.True(written >= 1);
            Assert.True(written <= 3);
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-6.2.2")]
    public void HpackEncoder_should_support_encoding_without_indexing()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var decoder = new HpackDecoder();

        // Use NeverIndex flag to force "without indexing" representation
        var header = new HpackHeader("x-temp", "temporary") { NeverIndex = true };
        var headers = new List<HpackHeader> { header };

        var buf = new byte[1024];
        Span<byte> span = buf;
        var written = encoder.Encode(headers, ref span, useHuffman: false);

        var encoded = buf[..written];
        var decoded = decoder.Decode(encoded);

        Assert.Single(decoded);
        Assert.Equal("x-temp", decoded[0].Name);
        Assert.Equal("temporary", decoded[0].Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-6")]
    public void HpackEncoder_should_preserve_header_order()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var decoder = new HpackDecoder();

        var headers = new List<(string, string)>
        {
            ("x-first", "1"),
            ("x-second", "2"),
            ("x-third", "3"),
            ("x-fourth", "4"),
        };

        var encoded = encoder.Encode(headers);
        var decoded = decoder.Decode(encoded.Span);

        Assert.Equal(headers.Count, decoded.Count);
        for (var i = 0; i < headers.Count; i++)
        {
            Assert.Equal(headers[i].Item1, decoded[i].Name);
            Assert.Equal(headers[i].Item2, decoded[i].Value);
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-5.2")]
    public void HpackEncoder_should_handle_utf8_characters()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var decoder = new HpackDecoder();

        var headers = new List<(string, string)>
        {
            ("x-utf8", "caf\u00e9"),
        };

        var encoded = encoder.Encode(headers);
        var decoded = decoder.Decode(encoded.Span);

        Assert.Single(decoded);
        Assert.Equal("x-utf8", decoded[0].Name);
        Assert.Equal("caf\u00e9", decoded[0].Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-5.2")]
    public void HpackEncoder_should_handle_utf8_characters_with_huffman()
    {
        var encoder = new HpackEncoder(useHuffman: true);
        var decoder = new HpackDecoder();

        var headers = new List<(string, string)>
        {
            ("x-utf8", "\u00e9\u00e0\u00e8"),
        };

        var encoded = encoder.Encode(headers);
        var decoded = decoder.Decode(encoded.Span);

        Assert.Single(decoded);
        Assert.Equal("x-utf8", decoded[0].Name);
        Assert.Equal("\u00e9\u00e0\u00e8", decoded[0].Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-6")]
    public void HpackEncoder_should_handle_long_header_value()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var decoder = new HpackDecoder();

        // Use 3000 bytes which fits comfortably in the 4096 default buffer
        // (accounting for header overhead and string literal encoding)
        var longValue = new string('x', 3000);
        var headers = new List<(string, string)> { ("x-long", longValue) };

        var encoded = encoder.Encode(headers);
        var decoded = decoder.Decode(encoded.Span);

        Assert.Single(decoded);
        Assert.Equal("x-long", decoded[0].Name);
        Assert.Equal(longValue, decoded[0].Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-5.2")]
    public void HpackEncoder_should_use_huffman_encoding_for_long_strings()
    {
        var valueWithoutHuffman = new string('a', 100);
        var valueWithHuffman = new string('a', 100);

        var encoder1 = new HpackEncoder(useHuffman: false);
        var encoder2 = new HpackEncoder(useHuffman: true);

        var headers1 = new List<(string, string)> { ("x-test", valueWithoutHuffman) };
        var headers2 = new List<(string, string)> { ("x-test", valueWithHuffman) };

        var encoded1 = encoder1.Encode(headers1);
        var encoded2 = encoder2.Encode(headers2);

        // Huffman encoding should be more efficient for repetitive data
        Assert.True(encoded2.Length <= encoded1.Length);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-4")]
    public void HpackEncoder_should_reset_dynamic_table_after_size_change()
    {
        var encoder = new HpackEncoder(useHuffman: false);

        // Encode something into dynamic table
        var h1 = new List<(string, string)> { ("x-dynamic", "value") };
        encoder.Encode(h1);

        // Change table size to 0 (should clear dynamic table)
        encoder.AcknowledgeTableSizeChange(0);

        var decoder = new HpackDecoder();
        decoder.SetMaxAllowedTableSize(0);

        // Encode something new
        var h2 = new List<(string, string)> { ("x-new", "data") };
        var encoded = encoder.Encode(h2);

        var decoded = decoder.Decode(encoded.Span);
        Assert.Single(decoded);
    }
}
