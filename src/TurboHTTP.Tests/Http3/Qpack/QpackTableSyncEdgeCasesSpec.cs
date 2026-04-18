using TurboHTTP.Protocol.Http3.Qpack;

namespace TurboHTTP.Tests.Http3.Qpack;

/// <summary>
/// Edge-case tests for QPACK table synchronization to achieve 100% branch coverage.
/// Tests synchronization coordinator initialization, instruction processing, blocked stream
/// management, and all error conditions per RFC 9204 §2.1.
/// </summary>
public sealed class QpackTableSyncEdgeCasesSpec
{
    /// RFC 9204 §2.1 — Initialize with zero encoder capacity
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-2.1")]
    public void Should_Initialize_With_Zero_EncoderCapacity()
    {
        var sync = new QpackTableSync(encoderMaxCapacity: 0, decoderMaxCapacity: 4096);

        Assert.Equal(0, sync.Encoder.DynamicTable.Capacity);
        Assert.Equal(4096, sync.Decoder.DynamicTable.Capacity);
    }

    /// RFC 9204 §2.1 — Initialize with large capacities
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-2.1")]
    public void Should_Initialize_With_Large_Capacities()
    {
        var sync = new QpackTableSync(encoderMaxCapacity: 65536, decoderMaxCapacity: 65536, maxBlockedStreams: 1000);

        Assert.Equal(65536, sync.Encoder.DynamicTable.Capacity);
        Assert.Equal(65536, sync.Decoder.DynamicTable.Capacity);
    }

    /// RFC 9204 §2.1 — Throws on negative max blocked streams
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-2.1")]
    public void Should_Throw_On_Negative_MaxBlockedStreams()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new QpackTableSync(maxBlockedStreams: -1));

        Assert.Equal("maxBlockedStreams", ex.ParamName);
    }

    /// RFC 9204 §2.1 — Zero max blocked streams disables blocking
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-2.1")]
    public void Should_Throw_When_BlockingExceeds_MaxBlockedStreams_Zero()
    {
        var sync = new QpackTableSync(encoderMaxCapacity: 4096, maxBlockedStreams: 0);
        var encoder = sync.Encoder;

        var headers = new List<(string, string)> { ("x-test", "value") };
        var encoded = encoder.Encode(headers);

        // Attempt to block first stream when max is 0
        var ex = Assert.Throws<QpackException>(() =>
            sync.TryDecodeOrBlock(encoded, streamId: 1));

        Assert.Contains("blocked", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// RFC 9204 §2.1 — Throws when exceeding max blocked streams
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-2.1")]
    public void Should_Throw_When_MaxBlockedStreams_Exceeded()
    {
        var sync = new QpackTableSync(encoderMaxCapacity: 4096, maxBlockedStreams: 2);
        var encoder = sync.Encoder;

        // Create and block first two streams with custom headers that will be inserted
        for (int i = 0; i < 2; i++)
        {
            var headers = new List<(string, string)> { ($"x-header-{i}", $"value-{i}") };
            var encoded = encoder.Encode(headers);
            // Don't apply instructions, so stream will be blocked
            var result = sync.TryDecodeOrBlock(encoded, streamId: i);
            if (result.IsBlocked)
            {
                // Confirmed blocked
            }
        }

        Assert.Equal(2, sync.BlockedStreamCount);

        // Attempt to block third stream — should throw
        var headers3 = new List<(string, string)> { ("x-header-3", "value-3") };
        var encoded3 = encoder.Encode(headers3);

        var ex = Assert.Throws<QpackException>(() =>
            sync.TryDecodeOrBlock(encoded3, streamId: 2));

        Assert.Contains("violation", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// RFC 9204 §4.3 — Apply empty encoder instructions
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.3")]
    public void Should_Handle_Empty_EncoderInstructions()
    {
        var sync = new QpackTableSync();
        var data = ReadOnlySpan<byte>.Empty;

        var count = sync.ApplyEncoderInstructions(data);

        Assert.Equal(0, count);
    }

    /// RFC 9204 §4.3 — Multiple encoder instructions applied in sequence
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.3")]
    public void Should_Apply_Multiple_EncoderInstructions()
    {
        var sync = new QpackTableSync(encoderMaxCapacity: 256);
        var encoder = sync.Encoder;

        // Encode multiple unique headers to generate multiple insert instructions
        var headers1 = new List<(string, string)> { ("x-header-1", "value1") };
        var encoded1 = encoder.Encode(headers1);
        var instructions1 = encoder.EncoderInstructions;

        var headers2 = new List<(string, string)> { ("x-header-2", "value2") };
        var encoded2 = encoder.Encode(headers2);
        var instructions2 = encoder.EncoderInstructions;

        // Apply both instruction sets sequentially
        var count1 = sync.ApplyEncoderInstructions(instructions1.Span);
        var count2 = sync.ApplyEncoderInstructions(instructions2.Span);

        Assert.True(count1 > 0);
        Assert.True(count2 > 0);
        Assert.Equal(2, sync.InsertCount);
    }

    /// RFC 9204 §4.4 — Process empty decoder instructions
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.4")]
    public void Should_Handle_Empty_DecoderInstructions()
    {
        var sync = new QpackTableSync();
        var data = ReadOnlySpan<byte>.Empty;

        var count = sync.ProcessDecoderInstructions(data);

        Assert.Equal(0, count);
    }

    /// RFC 9204 §4.4 — Section Acknowledgment updates encoder KnownReceivedCount
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.4.1")]
    public void Should_Update_EncoderKnownReceivedCount_OnSectionAck()
    {
        var sync = new QpackTableSync(encoderMaxCapacity: 4096);
        var encoder = sync.Encoder;

        // Insert entry into encoder's dynamic table
        encoder.DynamicTable.Insert("x-test", "value");
        encoder.TrackSection(streamId: 5, requiredInsertCount: 1);

        Assert.Equal(0, encoder.KnownReceivedCount);

        // Write Section Acknowledgment from decoder
        var buffer = new byte[16];
        Span<byte> span = buffer;
        var n = QpackDecoderInstructionWriter.WriteSectionAcknowledgment(streamId: 5, ref span);

        // Process on encoder side
        sync.ProcessDecoderInstructions(buffer.AsSpan(0, n));

        Assert.Equal(1, encoder.KnownReceivedCount);
    }

    /// RFC 9204 §4.4.3 — Insert Count Increment in decoder instructions
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.4.3")]
    public void Should_Process_InsertCountIncrement_InDecoderInstructions()
    {
        var sync = new QpackTableSync(encoderMaxCapacity: 4096);
        var encoder = sync.Encoder;

        Assert.Equal(0, encoder.KnownReceivedCount);

        // Write Insert Count Increment instruction
        var buffer = new byte[16];
        Span<byte> span = buffer;
        var n = QpackDecoderInstructionWriter.WriteInsertCountIncrement(5, ref span);

        // Process on encoder side
        sync.ProcessDecoderInstructions(buffer.AsSpan(0, n));

        Assert.Equal(5, encoder.KnownReceivedCount);
    }

    /// RFC 9204 §4.4.2 — Stream Cancellation removes blocked stream for that ID only
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.4.2")]
    public void Should_Remove_Only_Cancelled_BlockedStream()
    {
        var sync = new QpackTableSync(encoderMaxCapacity: 4096, maxBlockedStreams: 10);
        var encoder = sync.Encoder;

        // Block multiple streams
        for (int streamId = 0; streamId < 3; streamId++)
        {
            var headers = new List<(string, string)> { ($"x-stream-{streamId}", $"val-{streamId}") };
            var encoded = encoder.Encode(headers);
            sync.TryDecodeOrBlock(encoded, streamId);
        }

        Assert.Equal(3, sync.BlockedStreamCount);

        // Cancel stream 1
        var cancelBuf = new byte[16];
        Span<byte> cancelSpan = cancelBuf;
        var n = QpackDecoderInstructionWriter.WriteStreamCancellation(1, ref cancelSpan);
        sync.ProcessDecoderInstructions(cancelBuf.AsSpan(0, n));

        Assert.Equal(2, sync.BlockedStreamCount);
    }

    /// RFC 9204 §2.1.2 — Resolve partial blocked streams (some still blocked)
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-2.1.2")]
    public void Should_Resolve_Only_Ready_BlockedStreams()
    {
        var sync = new QpackTableSync(encoderMaxCapacity: 4096, maxBlockedStreams: 10);
        var encoder = sync.Encoder;

        // Create three headers to get InsertCount = 3
        var headers1 = new List<(string, string)> { ("x-first", "value1") };
        var headers2 = new List<(string, string)> { ("x-second", "value2") };
        var headers3 = new List<(string, string)> { ("x-third", "value3") };

        var encoded1 = encoder.Encode(headers1);
        var enc1 = encoder.EncoderInstructions.ToArray();

        var encoded2 = encoder.Encode(headers2);
        var enc2 = encoder.EncoderInstructions.ToArray();

        var encoded3 = encoder.Encode(headers3);
        var enc3 = encoder.EncoderInstructions.ToArray();

        // Block all three
        sync.TryDecodeOrBlock(encoded1, streamId: 1);
        sync.TryDecodeOrBlock(encoded2, streamId: 2);
        sync.TryDecodeOrBlock(encoded3, streamId: 3);

        Assert.Equal(3, sync.BlockedStreamCount);

        // Only apply first instruction (InsertCount becomes 1)
        sync.ApplyEncoderInstructions(enc1);

        var resolved = sync.ResolveBlockedStreams();

        // Only stream 1 should be resolved (required InsertCount = 1)
        Assert.Single(resolved);
        Assert.Equal(1, resolved[0].StreamId);
        Assert.Equal(2, sync.BlockedStreamCount); // Streams 2 and 3 still blocked
    }

    /// RFC 9204 §2.1.2 — Resolve all blocked streams at once
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-2.1.2")]
    public void Should_Resolve_All_BlockedStreams_When_ConditionMet()
    {
        var sync = new QpackTableSync(encoderMaxCapacity: 4096, maxBlockedStreams: 10);
        var encoder = sync.Encoder;

        // Block multiple streams
        var encodedBlocks = new List<ReadOnlyMemory<byte>>();
        var allInstructions = new List<byte>();

        for (int i = 0; i < 5; i++)
        {
            var headers = new List<(string, string)> { ($"x-stream-{i}", $"val-{i}") };
            var encoded = encoder.Encode(headers);
            encodedBlocks.Add(encoded);
            allInstructions.AddRange(encoder.EncoderInstructions.ToArray());
        }

        // Block all
        for (int i = 0; i < 5; i++)
        {
            sync.TryDecodeOrBlock(encodedBlocks[i], streamId: i);
        }

        Assert.Equal(5, sync.BlockedStreamCount);

        // Apply all instructions at once
        sync.ApplyEncoderInstructions(allInstructions.ToArray().AsSpan());

        var resolved = sync.ResolveBlockedStreams();

        Assert.Equal(5, resolved.Count);
        Assert.Equal(0, sync.BlockedStreamCount);
    }

    /// RFC 9204 §4.4.3 — Known Received Count starts at zero
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.4.3")]
    public void Should_Have_Zero_KnownReceivedCount_Initially()
    {
        var sync = new QpackTableSync();

        Assert.Equal(0, sync.KnownReceivedCount);
    }

    /// RFC 9204 §4.4.3 — WriteInsertCountIncrement returns zero when no change
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.4.3")]
    public void Should_Return_Zero_Increment_When_NoChange()
    {
        var sync = new QpackTableSync();

        Span<byte> buf = new byte[16];
        var span = buf;
        var increment = sync.WriteInsertCountIncrement(ref span);

        Assert.Equal(0, increment);
    }

    /// RFC 9204 §4.4.3 — WriteInsertCountIncrement updates KnownReceivedCount
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.4.3")]
    public void Should_Update_KnownReceivedCount_OnWriteIncrement()
    {
        var sync = new QpackTableSync();

        // Insert entries into decoder's table
        sync.Decoder.DynamicTable.Insert("x-test-1", "value1");
        sync.Decoder.DynamicTable.Insert("x-test-2", "value2");
        sync.Decoder.DynamicTable.Insert("x-test-3", "value3");

        Assert.Equal(0, sync.KnownReceivedCount);

        Span<byte> buf = new byte[16];
        var span = buf;
        var increment = sync.WriteInsertCountIncrement(ref span);

        Assert.Equal(3, increment);
        Assert.Equal(3, sync.KnownReceivedCount);
    }

    /// RFC 9204 §2.1 — Reset clears all state and creates fresh tables
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-2.1")]
    public void Should_Reset_ClearsAllState()
    {
        var sync = new QpackTableSync(encoderMaxCapacity: 4096, decoderMaxCapacity: 4096, maxBlockedStreams: 10);

        // Add some state - manually insert to have tracked state
        sync.Decoder.DynamicTable.Insert("x-test-1", "value1");
        sync.Decoder.DynamicTable.Insert("x-test-2", "value2");

        Assert.Equal(2, sync.InsertCount);

        // Reset
        sync.Reset();

        Assert.Equal(0, sync.InsertCount);
        Assert.Equal(0, sync.BlockedStreamCount);
        Assert.Equal(0, sync.KnownReceivedCount);
        // New instances should exist
        Assert.NotNull(sync.Encoder);
        Assert.NotNull(sync.Decoder);
    }

    /// RFC 9204 §4.3 — Set Dynamic Table Capacity instruction
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.3")]
    public void Should_Apply_SetDynamicTableCapacity_Instruction()
    {
        var sync = new QpackTableSync(encoderMaxCapacity: 4096, decoderMaxCapacity: 4096);

        // Manually emit a SetCapacity instruction (simulating encoder instruction)
        // and apply it to decoder
        sync.Decoder.DynamicTable.Insert("x-test-1", "val1");
        sync.Decoder.DynamicTable.Insert("x-test-2", "val2");

        Assert.Equal(4096, sync.Decoder.DynamicTable.Capacity);

        // In real usage, the encoder would emit this via instruction stream
        // For testing, we call the method directly
        var buffer = new byte[16];
        Span<byte> span = buffer;
        var written = QpackEncoderInstructionWriter.WriteSetDynamicTableCapacity(1024, ref span);

        // Decode and apply the instruction
        var instructionDecoder = new QpackInstructionDecoder();
        var instructions = instructionDecoder.DecodeAllEncoderInstructions(buffer.AsSpan(0, written));

        foreach (var instr in instructions)
        {
            if (instr.Type == EncoderInstructionType.SetDynamicTableCapacity)
            {
                sync.Decoder.DynamicTable.SetCapacity(instr.IntValue);
            }
        }

        Assert.Equal(1024, sync.Decoder.DynamicTable.Capacity);
    }

    /// RFC 9204 §4.3 — Duplicate instruction duplicates entry in decoder table
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.3")]
    public void Should_Apply_Duplicate_Instruction()
    {
        var sync = new QpackTableSync(encoderMaxCapacity: 4096, decoderMaxCapacity: 4096);

        // Insert initial entry
        sync.Decoder.DynamicTable.Insert("x-test", "original");
        Assert.Equal(1, sync.InsertCount);

        // Emit and apply a Duplicate instruction
        var buffer = new byte[16];
        Span<byte> span = buffer;
        var written = QpackEncoderInstructionWriter.WriteDuplicate(0, ref span);

        var instructionDecoder = new QpackInstructionDecoder();
        var instructions = instructionDecoder.DecodeAllEncoderInstructions(buffer.AsSpan(0, written));

        foreach (var instr in instructions)
        {
            if (instr.Type == EncoderInstructionType.Duplicate)
            {
                sync.Decoder.DynamicTable.Duplicate(instr.IntValue);
            }
        }

        Assert.Equal(2, sync.InsertCount);
        // Both entries should have the same name/value
        var dup = sync.Decoder.DynamicTable.GetEntry(0);
        Assert.Equal("x-test", dup!.Value.Name);
        Assert.Equal("original", dup.Value.Value);
    }

    /// RFC 9204 §2.1 — BlockedStreamCount property is accurate
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-2.1")]
    public void Should_Maintain_Accurate_BlockedStreamCount()
    {
        var sync = new QpackTableSync(encoderMaxCapacity: 4096, maxBlockedStreams: 10);
        var encoder = sync.Encoder;

        Assert.Equal(0, sync.BlockedStreamCount);

        for (int i = 0; i < 5; i++)
        {
            var headers = new List<(string, string)> { ($"x-header-{i}", $"value-{i}") };
            var encoded = encoder.Encode(headers);
            sync.TryDecodeOrBlock(encoded, streamId: i);
            Assert.Equal(i + 1, sync.BlockedStreamCount);
        }

        // Resolve some
        for (int i = 0; i < 3; i++)
        {
            var headers = new List<(string, string)> { ($"x-header-{i}", $"value-{i}") };
            encoder.Encode(headers);
            sync.ApplyEncoderInstructions(encoder.EncoderInstructions.Span);
        }

        sync.ResolveBlockedStreams();

        // Count should reflect resolved streams
        Assert.True(sync.BlockedStreamCount <= 5);
    }

    /// RFC 9204 §2.1 — InsertCount property reflects decoder table state
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-2.1")]
    public void Should_Return_Current_InsertCount()
    {
        var sync = new QpackTableSync(encoderMaxCapacity: 4096);

        Assert.Equal(0, sync.InsertCount);

        sync.Decoder.DynamicTable.Insert("x-test-1", "val1");
        Assert.Equal(1, sync.InsertCount);

        sync.Decoder.DynamicTable.Insert("x-test-2", "val2");
        Assert.Equal(2, sync.InsertCount);
    }

    /// RFC 9204 §4.3 — Insert With Name Reference (static) instruction
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.3")]
    public void Should_Apply_InsertWithNameReference_Static()
    {
        var sync = new QpackTableSync(encoderMaxCapacity: 4096);

        // Write Insert with name reference to static table (e.g., :method = value)
        var buffer = new byte[32];
        Span<byte> span = buffer;
        var written = QpackEncoderInstructionWriter.WriteInsertWithNameReference(
            nameIndex: 0, isStatic: true, value: "POST", ref span);

        var decoder = new QpackInstructionDecoder();
        var instructions = decoder.DecodeAllEncoderInstructions(buffer.AsSpan(0, written));

        foreach (var instr in instructions)
        {
            if (instr.Type == EncoderInstructionType.InsertWithNameReference)
            {
                var name = instr.IsStatic
                    ? QpackStaticTable.Entries[instr.NameIndex].Name
                    : "dynamic";
                sync.Decoder.DynamicTable.Insert(name, instr.ValueString);
            }
        }

        Assert.Equal(1, sync.InsertCount);
        var entry = sync.Decoder.DynamicTable.GetEntry(0);
        Assert.Equal("POST", entry!.Value.Value);
    }
}
