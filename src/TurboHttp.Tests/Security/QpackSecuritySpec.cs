using System.Buffers;
using System.Text;
using TurboHttp.Protocol.Http2.Hpack;
using TurboHttp.Protocol.Http3.Qpack;

namespace TurboHttp.Tests.Security;

/// <summary>
/// Tests QPACK resistance to resource exhaustion attacks, and cross-cutting memory
/// assertions for HPACK and QPACK dynamic tables. Verifies table capacity bounds,
/// blocked stream limit enforcement, integer overflow protection, and invariants
/// that apply to both compression schemes.
/// </summary>
/// <remarks>
/// Classes under test: <see cref="QpackDecoder"/>, <see cref="QpackEncoder"/>,
/// <see cref="QpackDynamicTable"/>, <see cref="QpackIntegerCodec"/>,
/// <see cref="HpackDecoder"/>, <see cref="HpackEncoder"/>, <see cref="HpackDynamicTable"/>.
/// Attack vectors: QPACK instruction flooding, blocked stream starvation,
/// integer overflow, capacity manipulation.
/// </remarks>
public sealed class QpackSecuritySpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204")]
    public void QpackDynamicTable_should_bound_table_size_when_encoder_instruction_flooding()
    {
        // Attack: Flood the QPACK dynamic table with many insert instructions.
        // Table must stay within configured capacity.
        const int capacity = 1024;
        var table = new QpackDynamicTable(capacity);

        // Insert 500 entries — table should evict and stay bounded
        for (var i = 0; i < 500; i++)
        {
            table.Insert($"header-{i}", $"value-{i:D4}");
        }

        Assert.True(table.CurrentSize <= capacity,
            $"QPACK table size {table.CurrentSize} exceeds capacity {capacity}");

        // Verify insert count is correct (all inserts counted even if evicted)
        Assert.Equal(500, table.InsertCount);

        // Verify recently inserted entries are still accessible
        var lastEntry = table.GetEntry(499);
        Assert.NotNull(lastEntry);
        Assert.Equal("header-499", lastEntry.Value.Name);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204")]
    public void QpackDynamicTable_should_store_no_entries_when_table_capacity_is_zero()
    {
        // Attack: Try to insert into a disabled (capacity=0) table
        var table = new QpackDynamicTable(0);

        var result = table.Insert("test", "value");

        Assert.Equal(-1, result); // Entry too large for table
        Assert.Equal(0, table.Count);
        Assert.Equal(0, table.CurrentSize);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204")]
    public void QpackDynamicTable_should_evict_old_entries_when_table_is_full()
    {
        // Attack: Fill table completely, then insert more — verify eviction
        const int capacity = 256;
        var table = new QpackDynamicTable(capacity);

        // Each entry: "h"(1) + "v"(1) + 32 = 34 bytes
        // 256 / 34 ≈ 7 entries
        for (var i = 0; i < 20; i++)
        {
            table.Insert("h", "v");
        }

        Assert.True(table.CurrentSize <= capacity);
        Assert.True(table.Count <= capacity / 34 + 1); // At most ~7-8 entries

        // Oldest entries should have been evicted
        var firstEntry = table.GetEntry(0);
        Assert.Null(firstEntry); // Evicted
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204")]
    public void QpackDynamicTable_should_evict_all_when_capacity_set_to_zero()
    {
        var table = new QpackDynamicTable(4096);

        for (var i = 0; i < 50; i++)
        {
            table.Insert($"header-{i}", $"value-{i}");
        }

        Assert.True(table.Count > 0);

        table.SetCapacity(0);

        Assert.Equal(0, table.Count);
        Assert.Equal(0, table.CurrentSize);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204")]
    public void QpackDecoder_should_throw_when_blocked_stream_limit_exceeded()
    {
        // Attack: Flood with header blocks requiring insert count > known,
        // exhausting blocked stream slots to starve legitimate streams.
        var decoder = new QpackDecoder(maxTableCapacity: 4096, maxBlockedStreams: 2);

        // Craft a header block with RequiredInsertCount > 0 (non-zero encoded RIC)
        // The table has no inserts, so any RIC > 0 will block.
        // MaxEntries = 4096 / 32 = 128
        // EncodedRIC = (RIC % (2 * 128)) + 1 = (1 % 256) + 1 = 2
        var block1 = new byte[]
        {
            0x02, // Encoded Required Insert Count = 2 → RIC = 1
            0x00, // S=0, delta base = 0
        };

        // First blocked stream — should succeed
        var result1 = decoder.TryDecode(block1, streamId: 1);
        Assert.True(result1.IsBlocked);

        // Second blocked stream — should succeed (limit is 2)
        var result2 = decoder.TryDecode(block1, streamId: 2);
        Assert.True(result2.IsBlocked);

        // Third blocked stream — should throw (exceeds limit of 2)
        var ex = Assert.Throws<QpackException>(() => decoder.TryDecode(block1, streamId: 3));
        Assert.Contains("Blocked stream limit", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204")]
    public void QpackDecoder_should_throw_immediately_when_blocked_stream_limit_is_zero()
    {
        // No blocking allowed — any RIC > known insert count is an error
        var decoder = new QpackDecoder(maxTableCapacity: 4096, maxBlockedStreams: 0);

        var block = new byte[]
        {
            0x02, // Encoded RIC = 2 → RIC = 1
            0x00, // S=0, delta base = 0
        };

        // With maxBlockedStreams=0 and using Decode (not TryDecode),
        // RIC > InsertCount should throw
        var ex = Assert.Throws<QpackException>(() => decoder.Decode(block));
        Assert.Contains("Required Insert Count", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204")]
    public void QpackDecoder_should_allow_new_streams_when_unblock_streams_called_after_limit()
    {
        var decoder = new QpackDecoder(maxTableCapacity: 4096, maxBlockedStreams: 1);

        var block = new byte[]
        {
            0x02, // Encoded RIC = 2 → RIC = 1
            0x00,
        };

        // Block one stream
        var result = decoder.TryDecode(block, streamId: 1);
        Assert.True(result.IsBlocked);
        Assert.Equal(1, decoder.BlockedStreamCount);

        // Unblock streams (simulating encoder state catch-up)
        decoder.UnblockStreams();
        Assert.Equal(0, decoder.BlockedStreamCount);

        // Should be able to block another stream now
        var result2 = decoder.TryDecode(block, streamId: 2);
        Assert.True(result2.IsBlocked);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204")]
    public void QpackIntegerCodec_should_throw_when_integer_overflows()
    {
        // Attack: Craft continuation bytes that push the decoded integer past MaxIntegerValue
        var malicious = new byte[]
        {
            0xFF, // prefix full (8-bit prefix = 255)
            0xFF, 0xFF, 0xFF, 0xFF, // continuation bytes
            0x7F // final byte
        };

        var pos = 0;
        var ex = Assert.Throws<QpackException>(
            () => QpackIntegerCodec.Decode(malicious, ref pos, 8));
        Assert.Contains("overflow", ex.Message.ToLowerInvariant());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204")]
    public void QpackIntegerCodec_should_throw_when_integer_encoding_too_long()
    {
        // Attack: Very long integer encoding (>9 continuation bytes)
        var malicious = new byte[20];
        malicious[0] = 0xFF; // prefix full
        for (var i = 1; i < 19; i++)
        {
            malicious[i] = 0x80; // continuation bit set, value 0
        }
        malicious[19] = 0x00; // stop bit

        var pos = 0;
        var ex = Assert.Throws<QpackException>(
            () => QpackIntegerCodec.Decode(malicious, ref pos, 8));
        Assert.Contains("overflow", ex.Message.ToLowerInvariant());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204")]
    public void QpackIntegerCodec_should_throw_when_integer_truncated()
    {
        // Attack: Integer with continuation bit set but no more data
        var malicious = new byte[]
        {
            0xFF, // prefix full
            0x80  // continuation bit set, no more bytes
        };

        var pos = 0;
        var ex = Assert.Throws<QpackException>(
            () => QpackIntegerCodec.Decode(malicious, ref pos, 8));
        Assert.Contains("truncated", ex.Message.ToLowerInvariant());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541")]
    public void HpackDynamicTable_should_never_exceed_max_size_after_1000_inserts()
    {
        // Memory assertion: table size is always <= MaxSize after every insert
        const int maxSize = 2048;
        var table = new HpackDynamicTable();
        table.SetMaxSize(maxSize);

        for (var i = 0; i < 1000; i++)
        {
            table.Add($"header-{i}", new string('x', i % 100));

            Assert.True(table.CurrentSize <= maxSize,
                $"HPACK table size {table.CurrentSize} exceeded max {maxSize} at insert {i}");
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204")]
    public void QpackDynamicTable_should_never_exceed_capacity_after_1000_inserts()
    {
        // Memory assertion: table size is always <= Capacity after every insert
        const int capacity = 2048;
        var table = new QpackDynamicTable(capacity);

        for (var i = 0; i < 1000; i++)
        {
            table.Insert($"header-{i}", new string('x', i % 100));

            Assert.True(table.CurrentSize <= capacity,
                $"QPACK table size {table.CurrentSize} exceeded capacity {capacity} at insert {i}");
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541")]
    public void HpackDecoder_should_reject_large_payloads_when_decoder_has_tight_limits()
    {
        // End-to-end: encoder produces valid HPACK, decoder with tight limits rejects
        var encoder = new HpackEncoder(useHuffman: false);
        var decoder = new HpackDecoder();
        decoder.SetMaxHeaderListSize(256);
        decoder.SetMaxStringLength(128);

        // Encode headers that would exceed the decoder's limits
        var headers = new[]
        {
            new HpackHeader("x-large", new string('A', 200))
        };
        var buf = new ArrayBufferWriter<byte>(512);
        encoder.Encode(headers, buf);

        // Decoder should reject due to string length limit (200 > 128)
        var ex = Assert.Throws<HpackException>(() => decoder.Decode(buf.WrittenSpan));
        Assert.Contains("§5.2", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541")]
    public void HpackDynamicTable_should_throw_when_negative_table_size()
    {
        var table = new HpackDynamicTable();
        var ex = Assert.Throws<HpackException>(() => table.SetMaxSize(-1));
        Assert.Contains("Invalid", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204")]
    public void QpackDynamicTable_should_throw_when_negative_table_capacity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new QpackDynamicTable(-1));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204")]
    public void QpackEncoder_should_bound_encoder_instructions_when_encoder_flooded()
    {
        // Attack: Many headers encoded with dynamic table → encoder instructions
        // should not grow unboundedly
        var encoder = new QpackEncoder(maxTableCapacity: 512);

        var headers = new List<(string Name, string Value)>();
        for (var i = 0; i < 50; i++)
        {
            headers.Add(($"x-hdr-{i}", $"val-{i}"));
        }

        var output = new ArrayBufferWriter<byte>(4096);
        encoder.Encode(headers, output);

        // Table should be bounded
        Assert.True(encoder.DynamicTable.CurrentSize <= 512,
            $"Encoder table size {encoder.DynamicTable.CurrentSize} exceeds capacity 512");

        // Encoder instructions were generated (non-empty)
        Assert.True(encoder.EncoderInstructions.Length > 0);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541")]
    public void HpackEncoder_should_never_index_sensitive_headers_when_table_under_pressure()
    {
        // Verify that Authorization, Cookie, etc. are never added to dynamic table
        // even when the table has ample room
        var encoder = new HpackEncoder(useHuffman: false);
        var decoder = new HpackDecoder();

        var headers = new[]
        {
            new HpackHeader("authorization", "Bearer secret-token-12345"),
            new HpackHeader("cookie", "session=abc123"),
            new HpackHeader("proxy-authorization", "Basic dXNlcjpwYXNz"),
            new HpackHeader("set-cookie", "id=xyz; Secure; HttpOnly"),
        };

        var buf = new ArrayBufferWriter<byte>(512);
        encoder.Encode(headers, buf);

        var decoded = decoder.Decode(buf.WrittenSpan);
        Assert.Equal(4, decoded.Count);

        // Verify all sensitive headers decoded correctly
        Assert.Equal("authorization", decoded[0].Name);
        Assert.Equal("Bearer secret-token-12345", decoded[0].Value);
        Assert.Equal("cookie", decoded[1].Name);
        Assert.Equal("session=abc123", decoded[1].Value);

        // Now encode a second block — sensitive headers should NOT be in dynamic table
        // so they should NOT be encoded as indexed references
        var buf2 = new ArrayBufferWriter<byte>(512);
        encoder.Encode(headers, buf2);

        // The second encoding should be roughly the same size as the first
        // (no compression benefit from dynamic table for sensitive headers)
        // Allow some tolerance for table size update overhead
        Assert.True(buf2.WrittenCount >= buf.WrittenCount - 10,
            "Sensitive headers appear to have been indexed — second encoding is suspiciously smaller");
    }
}
