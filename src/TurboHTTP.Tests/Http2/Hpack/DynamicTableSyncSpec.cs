using TurboHTTP.Protocol.Http2.Hpack;

namespace TurboHTTP.Tests.Http2.Hpack;

/// <summary>
/// Tests dynamic table size updates (§6.3), encoder table size changes, and encoder/decoder synchronization (§7.1).
/// </summary>
public sealed class DynamicTableSyncSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-6.3")]
    public void HpackDecoder_should_accept_table_size_update_when_update_appears_at_start_of_block()
    {
        var decoder = new HpackDecoder();
        decoder.SetMaxAllowedTableSize(512);

        var buf = new byte[64];
        Span<byte> span = buf;
        HpackEncoder.WriteInteger(512, prefixBits: 5, prefixFlags: 0x20, ref span);
        span[0] = 0x82;
        span = span[1..];

        var headers = decoder.Decode(buf.AsSpan(0, buf.Length - span.Length));
        Assert.Single(headers);
        Assert.Equal(":method", headers[0].Name);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-6.3")]
    public void HpackDecoder_should_accept_both_table_size_updates_when_two_updates_appear_at_start_of_block()
    {
        var decoder = new HpackDecoder();
        decoder.SetMaxAllowedTableSize(4096);

        var buf = new byte[64];
        Span<byte> span = buf;
        HpackEncoder.WriteInteger(256, prefixBits: 5, prefixFlags: 0x20, ref span);
        HpackEncoder.WriteInteger(4096, prefixBits: 5, prefixFlags: 0x20, ref span);
        span[0] = 0x82;
        span = span[1..];

        var headers = decoder.Decode(buf.AsSpan(0, buf.Length - span.Length));
        Assert.Single(headers);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-6.3")]
    public void HpackDecoder_should_throw_hpackexception_when_table_size_update_appears_after_indexed_header()
    {
        var decoder = new HpackDecoder();
        decoder.SetMaxAllowedTableSize(4096);

        var buf = new byte[64];
        Span<byte> span = buf;
        span[0] = 0x82;
        span = span[1..];
        HpackEncoder.WriteInteger(256, prefixBits: 5, prefixFlags: 0x20, ref span);
        var data = buf.AsSpan(0, buf.Length - span.Length).ToArray();

        var ex = Assert.Throws<HpackException>(() => decoder.Decode(data));
        Assert.Contains("6.3", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-6.3")]
    public void HpackDecoder_should_throw_hpackexception_when_table_size_update_appears_after_literal_with_indexing()
    {
        var decoder = new HpackDecoder();
        decoder.SetMaxAllowedTableSize(4096);

        var encoder = new HpackEncoder(useHuffman: false);
        var headerBlock = encoder.Encode(new List<(string, string)> { ("x-custom", "val") });

        var buf = new byte[256];
        headerBlock.Span.CopyTo(buf);
        var span = buf.AsSpan(headerBlock.Length);
        HpackEncoder.WriteInteger(512, prefixBits: 5, prefixFlags: 0x20, ref span);
        var totalWritten = buf.Length - span.Length;

        var ex = Assert.Throws<HpackException>(() => decoder.Decode(buf.AsSpan(0, totalWritten)));
        Assert.Contains("6.3", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-6.3")]
    public void HpackDecoder_should_throw_hpackexception_when_table_size_update_exceeds_settings()
    {
        var decoder = new HpackDecoder();
        decoder.SetMaxAllowedTableSize(256);

        var buf = new byte[16];
        Span<byte> span = buf;
        HpackEncoder.WriteInteger(257, prefixBits: 5, prefixFlags: 0x20, ref span);
        var data = buf.AsSpan(0, buf.Length - span.Length).ToArray();

        var ex = Assert.Throws<HpackException>(() => decoder.Decode(data));
        Assert.Contains("4.2", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-6.3")]
    public void HpackDecoder_should_accept_table_size_update_when_update_matches_exact_settings_value()
    {
        var decoder = new HpackDecoder();
        decoder.SetMaxAllowedTableSize(256);

        var buf = new byte[64];
        Span<byte> span = buf;
        HpackEncoder.WriteInteger(256, prefixBits: 5, prefixFlags: 0x20, ref span);
        span[0] = 0x82;
        span = span[1..];

        var headers = decoder.Decode(buf.AsSpan(0, buf.Length - span.Length));
        Assert.Single(headers);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-6.3")]
    public void HpackEncoder_should_emit_size_update_before_headers_when_acknowledge_table_size_change_is_called()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        encoder.AcknowledgeTableSizeChange(512);

        var encoded = encoder.Encode(new List<(string, string)> { (":method", "GET") });

        var firstByte = encoded.Span[0];
        Assert.Equal(0x20, firstByte & 0xE0);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-6.3")]
    public void HpackEncoder_should_emit_size_update_then_header_when_acknowledge_table_size_change_is_called()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var decoder = new HpackDecoder();

        encoder.AcknowledgeTableSizeChange(512);
        decoder.SetMaxAllowedTableSize(512);

        var encoded = encoder.Encode(new List<(string, string)> { (":method", "GET") });
        var decoded = decoder.Decode(encoded.Span);

        Assert.Single(decoded);
        Assert.Equal(":method", decoded[0].Name);
        Assert.Equal("GET", decoded[0].Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-6.3")]
    public void HpackEncoder_should_emit_zero_size_update_when_acknowledge_table_size_change_called_with_zero()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        encoder.AcknowledgeTableSizeChange(0);

        var encoded = encoder.Encode(new List<(string, string)> { (":method", "GET") });

        var firstByte = encoded.Span[0];
        Assert.Equal(0x20, firstByte);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-6.3")]
    public void HpackEncoder_should_throw_hpackexception_when_acknowledge_table_size_change_called_with_negative_value()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        Assert.Throws<HpackException>(() => encoder.AcknowledgeTableSizeChange(-1));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-6.3")]
    public void HpackEncoder_should_emit_size_update_only_once_when_acknowledge_table_size_change_is_called()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        encoder.AcknowledgeTableSizeChange(512);

        var firstBlock = encoder.Encode(new List<(string, string)> { (":method", "GET") });
        var secondBlock = encoder.Encode(new List<(string, string)> { (":method", "GET") });

        var secondFirstByte = secondBlock.Span[0];
        Assert.NotEqual(0x20, secondFirstByte & 0xE0);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-7.1")]
    public void HpackDecoder_should_make_dynamic_entry_accessible_via_index_when_encoder_adds_to_table()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var decoder = new HpackDecoder();

        var block1 = encoder.Encode(new List<(string, string)> { ("x-custom", "myval") });
        decoder.Decode(block1.Span);

        var block2 = encoder.Encode(new List<(string, string)> { ("x-custom", "myval") });
        var decoded2 = decoder.Decode(block2.Span);

        Assert.Single(decoded2);
        Assert.Equal("x-custom", decoded2[0].Name);
        Assert.Equal("myval", decoded2[0].Value);

        Assert.Equal(0x80, block2.Span[0] & 0x80);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-7.1")]
    public void HpackDecoder_should_maintain_fifo_indexing_when_multiple_dynamic_entries_are_added()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var decoder = new HpackDecoder();

        var h1 = new List<(string, string)> { ("x-first", "v1") };
        var h2 = new List<(string, string)> { ("x-second", "v2") };

        decoder.Decode(encoder.Encode(h1).Span);
        decoder.Decode(encoder.Encode(h2).Span);

        var block3 = encoder.Encode(h1);
        var decoded = decoder.Decode(block3.Span);
        Assert.Single(decoded);
        Assert.Equal("x-first", decoded[0].Name);
        Assert.Equal("v1", decoded[0].Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-7.1")]
    public void HpackDecoder_should_stay_in_sync_when_processing_multiple_header_blocks()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var decoder = new HpackDecoder();

        var allHeaders = new List<(string, string)>
        {
            (":method",    "GET"),
            (":path",      "/api/data"),
            (":scheme",    "https"),
            (":authority", "example.com"),
            ("accept",     "application/json"),
            ("x-trace-id", "abc-123"),
        };

        var block1 = encoder.Encode(allHeaders);
        var decoded1 = decoder.Decode(block1.Span);
        Assert.Equal(allHeaders.Count, decoded1.Count);

        var block2 = encoder.Encode(allHeaders);
        var decoded2 = decoder.Decode(block2.Span);
        Assert.Equal(allHeaders.Count, decoded2.Count);

        for (var i = 0; i < allHeaders.Count; i++)
        {
            Assert.Equal(allHeaders[i].Item1, decoded2[i].Name);
            Assert.Equal(allHeaders[i].Item2, decoded2[i].Value);
        }

        Assert.True(block2.Length <= block1.Length,
            $"Expected block2 ({block2.Length}B) <= block1 ({block1.Length}B)");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-7.1")]
    public void HpackDecoder_should_synchronize_table_size_change_when_both_sides_acknowledge()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var decoder = new HpackDecoder();

        var h = new List<(string, string)> { ("x-custom", "value") };
        decoder.Decode(encoder.Encode(h).Span);

        encoder.AcknowledgeTableSizeChange(0);
        decoder.SetMaxAllowedTableSize(4096);

        var block = encoder.Encode(new List<(string, string)> { (":method", "GET") });
        var decoded = decoder.Decode(block.Span);

        Assert.Single(decoded);
        Assert.Equal(":method", decoded[0].Name);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-7.1")]
    public void HpackDecoder_should_not_add_to_dynamic_table_when_never_indexed_header_is_encoded()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var decoder = new HpackDecoder();

        var headers = new List<HpackHeader>
        {
            new HpackHeader("authorization", "Bearer token123", NeverIndex: true),
        };

        var buf = new byte[256];
        Span<byte> span = buf;
        var written = encoder.Encode(headers, ref span, useHuffman: false);

        var encoded = buf.AsSpan(0, written);
        var decoded = decoder.Decode(encoded);
        Assert.Single(decoded);
        Assert.True(decoded[0].NeverIndex, "authorization must be decoded with NeverIndex=true");

        Assert.Equal(0x10, encoded[0] & 0xF0);
    }
}
