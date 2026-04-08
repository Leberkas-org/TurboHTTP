using System.Collections.Generic;
using Xunit;
using TurboHTTP.Protocol;
using TurboHTTP.Protocol.Http2.Hpack;

namespace TurboHTTP.Tests.Http2.Hpack;

/// <summary>
/// Tests HPACK encoding/decoding round-trips, integer representation, and string representation per RFC 7541.
/// Covers static table lookups, Huffman coding, WriteInteger/ReadInteger, and string literal handling.
/// </summary>
public sealed class HpackEncodingSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-A")]
    public void HpackEncoder_should_produce_single_byte_when_encoding_indexed_static_entry()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var headers = new List<(string, string)> { (":method", "GET") };
        var encoded = encoder.Encode(headers);

        Assert.Equal(1, encoded.Length);
        Assert.Equal(0x82, encoded.Span[0]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-6")]
    public void HpackEncoder_should_round_trip_correctly_when_encoding_and_decoding_pseudo_headers()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var decoder = new HpackDecoder();

        var headers = new List<(string, string)>
        {
            (":method", "GET"),
            (":path", "/index.html"),
            (":scheme", "https"),
            (":authority", "example.com"),
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
    public void HpackEncoder_should_round_trip_correctly_when_using_huffman_encoding()
    {
        var encoder = new HpackEncoder(useHuffman: true);
        var decoder = new HpackDecoder();

        var headers = new List<(string, string)>
        {
            (":method", "GET"),
            (":path", "/api/search?q=hello"),
            (":scheme", "https"),
            (":authority", "api.example.com"),
            ("content-type", "application/json"),
            ("authorization", "Bearer token123"),
            ("accept", "application/json, text/plain"),
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
    [Trait("RFC", "RFC7541-6.2")]
    public void HpackDecoder_should_decode_in_correct_order_when_decoding_literal_new_name()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var decoder = new HpackDecoder();

        var headers = new List<(string, string)>
        {
            ("x-custom-header", "my-value"),
            ("x-another", "data"),
        };

        var encoded = encoder.Encode(headers);
        var decoded = decoder.Decode(encoded.Span);

        Assert.Equal(2, decoded.Count);
        Assert.Equal("x-custom-header", decoded[0].Name);
        Assert.Equal("my-value", decoded[0].Value);
        Assert.Equal("x-another", decoded[1].Name);
        Assert.Equal("data", decoded[1].Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-6.3")]
    public void HpackDecoder_should_respect_dynamic_table_size_update_when_decoding_headers()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var decoder = new HpackDecoder();

        var h1 = new List<(string, string)> { ("x-test", "value") };
        var e1 = encoder.Encode(h1);
        decoder.Decode(e1.Span);

        var h2 = new List<(string, string)> { ("x-fresh", "new") };

        var sizeUpdate = new byte[] { 0x20 };
        var encodedHeader = encoder.Encode(h2);

        var combined = new byte[sizeUpdate.Length + encodedHeader.Length];
        sizeUpdate.CopyTo(combined, 0);
        encodedHeader.Span.CopyTo(combined.AsSpan(sizeUpdate.Length));

        var decoded = decoder.Decode(combined);
        Assert.Single(decoded);
        Assert.Equal("x-fresh", decoded[0].Name);
        Assert.Equal("new", decoded[0].Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-5.1")]
    public void HpackDecoder_should_read_single_byte_when_integer_fits_in_prefix()
    {
        var data = new byte[] { 0x05 };
        var pos = 0;
        var result = HpackDecoder.ReadInteger(data, ref pos, prefixBits: 5);

        Assert.Equal(5, result);
        Assert.Equal(1, pos);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-5.1")]
    public void HpackDecoder_should_decode_multibyte_integer_correctly_when_multiple_byte_encoding_is_used()
    {
        var data = new byte[] { 0x1F, 0x9A, 0x0A };
        var pos = 0;
        var result = HpackDecoder.ReadInteger(data, ref pos, prefixBits: 5);

        Assert.Equal(1337, result);
        Assert.Equal(3, pos);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-5.1")]
    public void HpackDecoder_should_accept_max_value_when_reading_integer()
    {
        var maxValue = (1 << 28) - 1;
        var buf = new byte[16];
        Span<byte> span = buf;
        var written = HpackEncoder.WriteInteger(maxValue, prefixBits: 5, prefixFlags: 0x00, ref span);

        var encoded = buf[..written];
        var pos = 0;
        var result = HpackDecoder.ReadInteger(encoded, ref pos, prefixBits: 5);

        Assert.Equal(maxValue, result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-5.1")]
    public void HpackDecoder_should_throw_hpackexception_when_integer_overflows()
    {
        var data = new byte[]
        {
            0x1F,
            0xFF,
            0xFF,
            0xFF,
            0xFF,
            0xFF,
        };
        var pos = 0;
        var ex = Assert.Throws<HpackException>(() =>
            HpackDecoder.ReadInteger(data, ref pos, prefixBits: 5));
        Assert.Contains("overflow", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-5.1")]
    public void HpackDecoder_should_throw_hpackexception_when_integer_data_is_truncated()
    {
        var truncated = new byte[] { 0x1F, 0x80 };
        var pos = 0;
        var ex = Assert.Throws<HpackException>(() =>
            HpackDecoder.ReadInteger(truncated, ref pos, prefixBits: 5));
        Assert.Contains("truncated", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-C.2")]
    public void HpackDecoder_should_decode_first_request_when_decoding_appendix_c2_without_huffman()
    {
        var encoded = new byte[]
        {
            0x82,
            0x86,
            0x84,
            0x41, 0x0f,
            (byte)'w', (byte)'w', (byte)'w', (byte)'.', (byte)'e', (byte)'x', (byte)'a', (byte)'m',
            (byte)'p', (byte)'l', (byte)'e', (byte)'.', (byte)'c', (byte)'o', (byte)'m',
        };

        var decoder = new HpackDecoder();
        var decoded = decoder.Decode(encoded);

        Assert.Equal(4, decoded.Count);
        Assert.Equal(":method", decoded[0].Name);
        Assert.Equal("GET", decoded[0].Value);
        Assert.Equal(":scheme", decoded[1].Name);
        Assert.Equal("http", decoded[1].Value);
        Assert.Equal(":path", decoded[2].Name);
        Assert.Equal("/", decoded[2].Value);
        Assert.Equal(":authority", decoded[3].Name);
        Assert.Equal("www.example.com", decoded[3].Value);
    }

    public static IEnumerable<object[]> StaticTableEntries()
    {
        for (var i = 1; i <= HpackStaticTable.StaticCount; i++)
        {
            var (name, value) = HpackStaticTable.Entries[i];
            yield return [i, name, value];
        }
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC7541-A")]
    [MemberData(nameof(StaticTableEntries))]
    public void HpackEncoder_should_round_trip_as_indexed_representation_when_encoding_and_decoding_static_table_entry(int index, string name, string value)
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var decoder = new HpackDecoder();

        var encoded = encoder.Encode(new List<(string, string)> { (name, value) });
        var decoded = decoder.Decode(encoded.Span);

        Assert.Single(decoded);
        Assert.Equal(name, decoded[0].Name);
        Assert.Equal(value, decoded[0].Value);
        _ = index;
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-5.1")]
    public void HpackEncoder_should_encode_in_one_byte_when_integer_is_smaller_than_prefix_limit()
    {
        var buf = new byte[16];
        Span<byte> span = buf;
        var written = HpackEncoder.WriteInteger(10, prefixBits: 5, prefixFlags: 0x00, ref span);

        Assert.Equal(1, written);
        Assert.Equal(10, buf[0]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-5.1")]
    public void HpackEncoder_should_require_continuation_bytes_when_integer_is_at_prefix_limit()
    {
        var buf = new byte[16];
        Span<byte> span = buf;
        var written = HpackEncoder.WriteInteger(31, prefixBits: 5, prefixFlags: 0x00, ref span);

        Assert.Equal(2, written);
        Assert.Equal(0x1F, buf[0]);
        Assert.Equal(0x00, buf[1]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-5.1")]
    public void HpackEncoder_should_round_trip_max_value_when_integer_is_2147483647()
    {
        const int max = int.MaxValue;
        var buf = new byte[16];
        Span<byte> span = buf;
        var written = HpackEncoder.WriteInteger(max, prefixBits: 5, prefixFlags: 0x00, ref span);

        var encoded = buf[..written];
        var pos = 0;
        var decoded = HpackDecoder.ReadInteger(encoded, ref pos, prefixBits: 5);

        Assert.Equal(max, decoded);
        Assert.Equal(encoded.Length, pos);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-5.1")]
    public void HpackDecoder_should_throw_hpackexception_when_integer_exceeds_max_int()
    {
        var bytes = new byte[] { 0x1F, 0xE1, 0xFF, 0xFF, 0xFF, 0x07 };
        var pos = 0;
        var ex = Assert.Throws<HpackException>(() =>
            HpackDecoder.ReadInteger(bytes, ref pos, prefixBits: 5));
        Assert.Contains("overflow", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC7541-5.1")]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void HpackEncoder_should_handle_boundary_values_when_encoding_integer_with_various_prefix_bits(int bits)
    {
        var limit = (1 << bits) - 1;

        if (limit > 0)
        {
            var buf1 = new byte[16];
            Span<byte> span1 = buf1;
            HpackEncoder.WriteInteger(limit - 1, prefixBits: bits, prefixFlags: 0x00, ref span1);
            Assert.Equal(1, 16 - span1.Length);
            var p1 = 0;
            Assert.Equal(limit - 1, HpackDecoder.ReadInteger(buf1, ref p1, bits));
        }

        {
            var buf2 = new byte[16];
            Span<byte> span2 = buf2;
            HpackEncoder.WriteInteger(limit, prefixBits: bits, prefixFlags: 0x00, ref span2);
            var written2 = 16 - span2.Length;
            var p2 = 0;
            Assert.Equal(limit, HpackDecoder.ReadInteger(buf2[..written2], ref p2, bits));
        }

        {
            var buf3 = new byte[16];
            Span<byte> span3 = buf3;
            HpackEncoder.WriteInteger(limit + 1, prefixBits: bits, prefixFlags: 0x00, ref span3);
            var written3 = 16 - span3.Length;
            var p3 = 0;
            Assert.Equal(limit + 1, HpackDecoder.ReadInteger(buf3[..written3], ref p3, bits));
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-5.2")]
    public void HpackDecoder_should_decode_plain_string_when_decoding_string_literal()
    {
        var raw = new byte[] { 0x00, 0x01, (byte)'a', 0x05, (byte)'h', (byte)'e', (byte)'l', (byte)'l', (byte)'o' };
        var decoder = new HpackDecoder();
        var decoded = decoder.Decode(raw);

        Assert.Single(decoded);
        Assert.Equal("a", decoded[0].Name);
        Assert.Equal("hello", decoded[0].Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-5.2")]
    public void HpackDecoder_should_decode_huffman_string_when_decoding_string_literal()
    {
        var huffBuf = new byte[HuffmanCodec.GetMaxEncodedLength(5)];
        var huffLen = HuffmanCodec.Encode("hello"u8, huffBuf);
        var huffBytes = huffBuf[..huffLen].ToArray();

        var nameRaw = new byte[] { 0x01, (byte)'a' };
        var valHuff = new byte[1 + huffBytes.Length];
        valHuff[0] = (byte)(0x80 | huffBytes.Length);
        huffBytes.CopyTo(valHuff, 1);

        var packet = new byte[1 + nameRaw.Length + valHuff.Length];
        packet[0] = 0x00;
        nameRaw.CopyTo(packet, 1);
        valHuff.CopyTo(packet, 1 + nameRaw.Length);

        var decoder = new HpackDecoder();
        var decoded = decoder.Decode(packet);

        Assert.Single(decoded);
        Assert.Equal("a", decoded[0].Name);
        Assert.Equal("hello", decoded[0].Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-5.2")]
    public void HpackDecoder_should_decode_empty_string_when_decoding_string_literal()
    {
        var raw = new byte[] { 0x00, 0x01, (byte)'a', 0x00 };
        var decoder = new HpackDecoder();
        var decoded = decoder.Decode(raw);

        Assert.Single(decoded);
        Assert.Equal("a", decoded[0].Name);
        Assert.Equal(string.Empty, decoded[0].Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-5.2")]
    public void HpackDecoder_should_decode_without_truncation_when_string_literal_is_larger_than_8kb()
    {
        const int valueLen = 9000;
        var valueStr = new string('x', valueLen);
        var valueBytes = System.Text.Encoding.ASCII.GetBytes(valueStr);

        var bytes = new List<byte> { 0x00, 0x01, (byte)'a', 0x7F, 0xA9, 0x45 };
        bytes.AddRange(valueBytes);

        var decoder = new HpackDecoder();
        var decoded = decoder.Decode(bytes.ToArray());

        Assert.Single(decoded);
        Assert.Equal("a", decoded[0].Name);
        Assert.Equal(valueStr, decoded[0].Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-5.2")]
    public void HpackDecoder_should_throw_hpackexception_when_huffman_data_is_malformed()
    {
        var raw = new byte[] { 0x00, 0x01, (byte)'a', 0x83, 0xFF, 0xFF, 0xFF };
        var decoder = new HpackDecoder();
        Assert.Throws<HpackException>(() => decoder.Decode(raw));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-5.2")]
    public void HpackDecoder_should_throw_hpackexception_when_eos_padding_bits_are_not_all_ones()
    {
        var raw = new byte[] { 0x00, 0x01, (byte)'a', 0x81, 0x06 };
        var decoder = new HpackDecoder();
        Assert.Throws<HpackException>(() => decoder.Decode(raw));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-5.2")]
    public void HpackDecoder_should_throw_hpackexception_when_eos_padding_exceeds_seven_bits()
    {
        var raw = new byte[] { 0x00, 0x01, (byte)'a', 0x82, 0x00, 0x00 };
        var decoder = new HpackDecoder();
        Assert.Throws<HpackException>(() => decoder.Decode(raw));
    }
}
