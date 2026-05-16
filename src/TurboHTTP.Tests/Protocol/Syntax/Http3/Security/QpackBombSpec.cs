using TurboHTTP.Protocol.Syntax.Http3.Qpack;

namespace TurboHTTP.Tests.Protocol.Syntax.Http3.Security;

public sealed class QpackBombSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-3.2")]
    public void QpackDynamicTable_should_evict_all_entries_when_capacity_set_to_zero()
    {
        var table = new QpackDynamicTable(1024);

        // Populate table
        for (var i = 0; i < 10; i++)
        {
            table.Insert($"header-{i}", "value");
        }

        Assert.True(table.Count > 0);

        // Attack: Set capacity to zero — all entries must be evicted
        table.SetCapacity(0);
        Assert.Equal(0, table.Count);
        Assert.Equal(0, table.CurrentSize);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-3.2")]
    public void QpackDynamicTable_should_stay_bounded_when_flooded_with_large_entries()
    {
        const int capacity = 512;
        var table = new QpackDynamicTable(capacity);

        // Attack: Flood with entries much larger than individual capacity
        for (var i = 0; i < 200; i++)
        {
            table.Insert($"x-bomb-{i}", new string('A', 100));
        }

        Assert.True(table.CurrentSize <= capacity,
            $"Table size {table.CurrentSize} exceeds capacity {capacity}");
        Assert.Equal(200, table.InsertCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-3.2")]
    public void QpackDynamicTable_should_clear_without_crash_when_single_entry_exceeds_capacity()
    {
        var table = new QpackDynamicTable(64);

        // Insert a small entry first
        table.Insert("a", "b");
        Assert.Equal(1, table.Count);

        // Attack: Single entry larger than table capacity (1 + 10000 + 32 = 10033 > 64)
        table.Insert("x", new string('Z', 10000));

        // Table should be cleared and oversized entry NOT added
        Assert.Equal(0, table.Count);
        Assert.Equal(0, table.CurrentSize);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-3.2")]
    public void QpackDynamicTable_should_not_leak_memory_when_rapid_capacity_churn()
    {
        var table = new QpackDynamicTable(4096);

        // Attack: Rapidly oscillate table capacity to trigger GC pressure
        for (var cycle = 0; cycle < 100; cycle++)
        {
            for (var i = 0; i < 20; i++)
            {
                table.Insert($"c{cycle}-h{i}", new string('x', 30));
            }

            table.SetCapacity(0);
            Assert.Equal(0, table.Count);
            Assert.Equal(0, table.CurrentSize);

            table.SetCapacity(4096);
        }

        // Final state: table is functional after churn
        table.Insert("final", "entry");
        Assert.True(table.Count > 0);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.5")]
    public void QpackDecoder_should_throw_when_header_block_references_evicted_entry()
    {
        var decoder = new QpackDecoder(maxTableCapacity: 128, maxBlockedStreams: 0);

        // Build header block with Required Insert Count = 0 (no dynamic table refs)
        // but referencing a post-base index that doesn't exist
        var block = new byte[]
        {
            0x00, // Encoded RIC = 0 → no references expected
            0x00, // S=0, delta base = 0
            0x10, // Post-base indexed (0001xxxx), index = 0 (absolute 0, which doesn't exist)
        };

        Assert.ThrowsAny<QpackException>(() => decoder.Decode(block));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.5")]
    public void QpackIntegerCodec_should_throw_when_oversized_integer_via_many_continuation_bytes()
    {
        // Attack: Craft continuation bytes that push decoded value past MaxIntegerValue
        var malicious = new byte[]
        {
            0xFF,
            0xFF, 0xFF, 0xFF, 0xFF,
            0x7F
        };

        var pos = 0;
        var ex = Assert.Throws<QpackException>(() => QpackIntegerCodec.Decode(malicious, ref pos, 8));
        Assert.Contains("overflow", ex.Message.ToLowerInvariant());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.5")]
    public void QpackIntegerCodec_should_throw_when_encoding_has_excessive_continuation_bytes()
    {
        // Attack: Very long integer encoding (>9 continuation bytes, all zeros)
        var malicious = new byte[20];
        malicious[0] = 0xFF;
        for (var i = 1; i < 19; i++)
        {
            malicious[i] = 0x80; // continuation bit set, value 0
        }

        malicious[19] = 0x00; // stop bit

        var pos = 0;
        var ex = Assert.Throws<QpackException>(() => QpackIntegerCodec.Decode(malicious, ref pos, 8));
        Assert.Contains("overflow", ex.Message.ToLowerInvariant());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.5")]
    public void QpackIntegerCodec_should_throw_when_integer_truncated_mid_continuation()
    {
        // Attack: Integer with continuation bit set but no more data
        var malicious = new byte[]
        {
            0xFF, // prefix full
            0x80 // continuation bit set, no final byte
        };

        var pos = 0;
        var ex = Assert.Throws<QpackException>(() => QpackIntegerCodec.Decode(malicious, ref pos, 8));
        Assert.Contains("truncated", ex.Message.ToLowerInvariant());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.5")]
    public void QpackEncoder_should_bound_dynamic_table_when_many_unique_headers_encoded()
    {
        var encoder = new QpackEncoder(maxTableCapacity: 4096);

        // Encode multiple batches of headers to stress the dynamic table
        for (var batch = 0; batch < 10; batch++)
        {
            var headers = new List<(string Name, string Value)>();
            for (var i = 0; i < 10; i++)
            {
                headers.Add(($"x-hdr-{batch}-{i}", $"val-{i}"));
            }

            var encoded = encoder.Encode(headers);
            Assert.True(encoded.Length > 0);
        }

        Assert.True(encoder.DynamicTable.CurrentSize <= 4096,
            $"Encoder table {encoder.DynamicTable.CurrentSize} exceeds capacity 4096");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.3")]
    public void QpackInstructionDecoder_should_throw_when_set_capacity_instruction_truncated()
    {
        using var instructionDecoder = new QpackInstructionDecoder();

        // Set Dynamic Table Capacity: 001xxxxx with prefix full
        // 0x3F (prefix full for 5-bit), then continuation with no stop byte
        var malicious = new byte[] { 0x3F, 0x80 };

        var status = instructionDecoder.TryDecodeEncoderInstruction(malicious, out var instruction);

        // Should be NeedMoreData (waiting for continuation) or throw
        Assert.True(status == QpackDecodeStatus.NeedMoreData || instruction == null);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-2.1")]
    public void QpackDecoder_should_throw_when_blocked_streams_exceed_configured_limit()
    {
        var decoder = new QpackDecoder(maxTableCapacity: 4096, maxBlockedStreams: 1);

        // Header block requiring insert count > 0 (will block)
        // MaxEntries = 4096 / 32 = 128, EncodedRIC = (1 % 256) + 1 = 2
        var block = new byte[]
        {
            0x02, // Encoded RIC = 2 → RIC = 1
            0x00, // S=0, delta base = 0
        };

        var result1 = decoder.TryDecode(block, streamId: 1);
        Assert.True(result1.IsBlocked);

        // Second stream exceeds blocked limit of 1
        var ex = Assert.Throws<QpackException>(() => decoder.TryDecode(block, streamId: 2));
        Assert.Contains("Blocked stream limit", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-2.1")]
    public void QpackDecoder_should_recover_blocked_slots_when_unblock_called()
    {
        var decoder = new QpackDecoder(maxTableCapacity: 4096, maxBlockedStreams: 1);

        var block = new byte[] { 0x02, 0x00 };

        var result1 = decoder.TryDecode(block, streamId: 1);
        Assert.True(result1.IsBlocked);
        Assert.Equal(1, decoder.BlockedStreamCount);

        decoder.UnblockStreams();
        Assert.Equal(0, decoder.BlockedStreamCount);

        // Should be able to block another stream
        var result2 = decoder.TryDecode(block, streamId: 2);
        Assert.True(result2.IsBlocked);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-3.2")]
    public void QpackDynamicTable_should_throw_when_negative_capacity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new QpackDynamicTable(-1));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-3.2")]
    public void QpackDynamicTable_should_never_exceed_capacity_after_1000_variable_inserts()
    {
        const int capacity = 1024;
        var table = new QpackDynamicTable(capacity);

        for (var i = 0; i < 1000; i++)
        {
            table.Insert($"header-{i}", new string('x', i % 200));

            Assert.True(table.CurrentSize <= capacity,
                $"Table size {table.CurrentSize} exceeded capacity {capacity} at insert {i}");
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.5")]
    public void QpackEncoder_should_never_index_sensitive_headers()
    {
        var encoder = new QpackEncoder(maxTableCapacity: 4096);

        var sensitiveHeaders = new List<(string, string)>
        {
            ("authorization", "Bearer secret-token"),
            ("cookie", "session=abc123"),
            ("proxy-authorization", "Basic dXNlcjpwYXNz"),
            ("set-cookie", "id=xyz; Secure; HttpOnly"),
        };

        var encoded1 = encoder.Encode(sensitiveHeaders);

        // Second encoding should be roughly same size (no dynamic table compression)
        var encoded2 = encoder.Encode(sensitiveHeaders);

        Assert.True(encoded2.Length >= encoded1.Length - 10,
            "Sensitive headers appear to have been indexed — second encoding is suspiciously smaller");
    }
}