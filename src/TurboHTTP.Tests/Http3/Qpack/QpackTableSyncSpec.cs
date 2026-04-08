using TurboHTTP.Protocol.Http3.Qpack;

namespace TurboHTTP.Tests.Http3.Qpack;

/// <summary>
/// Tests for QPACK table synchronization per RFC 9204 §2.1.1, §2.1.2.
/// Verifies that encoder and decoder dynamic tables stay synchronized via
/// instruction streams, insert count is tracked across multiple header blocks,
/// and blocked streams are resolved when the required insert count is reached.
/// </summary>
public sealed class QpackTableSyncSpec
{

    /// RFC 9204 §2.1.1 — Encoder instructions synchronize decoder dynamic table
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-2.1.1")]
    public void Should_SyncDecoderTable_ViaEncoderInstructions()
    {
        var encoder = new QpackEncoder(maxTableCapacity: 4096);
        var decoder = new QpackDecoder(maxTableCapacity: 4096);
        var sync = new QpackTableSync(decoder);

        var headers = new List<(string, string)>
        {
            ("x-custom-header", "custom-value-1"),
            ("x-another", "another-value"),
        };

        // Encode — this produces encoder instructions as a side effect
        var encoded = encoder.Encode(headers);

        // Apply encoder instructions to decoder via sync
        var applied = sync.ApplyEncoderInstructions(encoder.EncoderInstructions.Span);

        Assert.True(applied > 0);
        Assert.Equal(encoder.DynamicTable.InsertCount, decoder.DynamicTable.InsertCount);

        // Now decode should succeed
        var decoded = decoder.Decode(encoded.Span);

        Assert.Equal(headers.Count, decoded.Count);
        for (var i = 0; i < headers.Count; i++)
        {
            Assert.Equal(headers[i].Item1, decoded[i].Name);
            Assert.Equal(headers[i].Item2, decoded[i].Value);
        }
    }

    /// RFC 9204 §2.1.1 — Insert count tracked across multiple header blocks
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-2.1.1")]
    public void Should_TrackInsertCount_AcrossMultipleHeaderBlocks()
    {
        var encoder = new QpackEncoder(maxTableCapacity: 4096);
        var decoder = new QpackDecoder(maxTableCapacity: 4096);
        var sync = new QpackTableSync(decoder);

        // First header block
        var headers1 = new List<(string, string)>
        {
            ("x-header-a", "value-a"),
            ("x-header-b", "value-b"),
        };
        var encoded1 = encoder.Encode(headers1);
        sync.ApplyEncoderInstructions(encoder.EncoderInstructions.Span);

        Assert.Equal(2, sync.InsertCount);

        var decoded1 = decoder.Decode(encoded1.Span);
        Assert.Equal(2, decoded1.Count);

        // Second header block with new headers
        var headers2 = new List<(string, string)>
        {
            ("x-header-c", "value-c"),
            ("x-header-d", "value-d"),
        };
        var encoded2 = encoder.Encode(headers2);
        sync.ApplyEncoderInstructions(encoder.EncoderInstructions.Span);

        Assert.Equal(4, sync.InsertCount);

        var decoded2 = decoder.Decode(encoded2.Span);
        Assert.Equal(2, decoded2.Count);
        Assert.Equal("x-header-c", decoded2[0].Name);
        Assert.Equal("x-header-d", decoded2[1].Name);
    }

    /// RFC 9204 §2.1.1 — Reused headers skip inserts on second encode
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-2.1.1")]
    public void Should_SkipInserts_WhenHeadersAlreadyInTable()
    {
        var encoder = new QpackEncoder(maxTableCapacity: 4096);
        var decoder = new QpackDecoder(maxTableCapacity: 4096);
        var sync = new QpackTableSync(decoder);

        var headers = new List<(string, string)>
        {
            ("x-session", "sess-001"),
        };

        // First encode: inserts into dynamic table
        var encoded1 = encoder.Encode(headers);
        sync.ApplyEncoderInstructions(encoder.EncoderInstructions.Span);
        Assert.Equal(1, sync.InsertCount);

        var decoded1 = decoder.Decode(encoded1.Span);
        Assert.Equal("x-session", decoded1[0].Name);

        // Second encode: same header should reuse existing entry, no new instructions
        var encoded2 = encoder.Encode(headers);
        Assert.Equal(0, encoder.EncoderInstructions.Length);

        var decoded2 = decoder.Decode(encoded2.Span);
        Assert.Equal("x-session", decoded2[0].Name);

        // Insert count unchanged
        Assert.Equal(1, sync.InsertCount);
    }


    /// RFC 9204 §2.1.2 — Stream blocks when required insert count exceeds known
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-2.1.2")]
    public void Should_BlockStream_WhenRequiredInsertCountExceedsKnown()
    {
        var encoder = new QpackEncoder(maxTableCapacity: 4096);
        var decoder = new QpackDecoder(maxTableCapacity: 4096, maxBlockedStreams: 10);
        var sync = new QpackTableSync(decoder, maxBlockedStreams: 10);

        var headers = new List<(string, string)>
        {
            ("x-new-header", "new-value"),
        };

        // Encode produces header block referencing dynamic table
        var encoded = encoder.Encode(headers);

        // Do NOT apply encoder instructions — decoder's table is behind
        // TryDecodeOrBlock should detect the block
        var result = sync.TryDecodeOrBlock(encoded, streamId: 4);

        Assert.True(result.IsBlocked);
        Assert.True(result.RequiredInsertCount > 0);
        Assert.Equal(1, sync.BlockedStreamCount);
    }

    /// RFC 9204 §2.1.2 — Blocked stream resolved when insert count reached
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-2.1.2")]
    public void Should_ResolveBlockedStream_WhenInsertCountReached()
    {
        var encoder = new QpackEncoder(maxTableCapacity: 4096);
        var decoder = new QpackDecoder(maxTableCapacity: 4096, maxBlockedStreams: 10);
        var sync = new QpackTableSync(decoder, maxBlockedStreams: 10);

        var headers = new List<(string, string)>
        {
            ("x-resolve-me", "resolved-value"),
        };

        var encoded = encoder.Encode(headers);

        // Block the stream first (no table sync yet)
        var result = sync.TryDecodeOrBlock(encoded, streamId: 8);
        Assert.True(result.IsBlocked);

        // Now apply encoder instructions → decoder table catches up
        sync.ApplyEncoderInstructions(encoder.EncoderInstructions.Span);

        // Resolve blocked streams
        var resolved = sync.ResolveBlockedStreams();

        Assert.Single(resolved);
        Assert.Equal(8, resolved[0].StreamId);
        Assert.Single(resolved[0].Headers);
        Assert.Equal("x-resolve-me", resolved[0].Headers[0].Name);
        Assert.Equal("resolved-value", resolved[0].Headers[0].Value);
        Assert.Equal(0, sync.BlockedStreamCount);
    }

    /// RFC 9204 §2.1.2 — Multiple blocked streams resolved in batch
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-2.1.2")]
    public void Should_ResolveMultipleBlockedStreams_InBatch()
    {
        var encoder = new QpackEncoder(maxTableCapacity: 4096);
        var decoder = new QpackDecoder(maxTableCapacity: 4096, maxBlockedStreams: 10);
        var sync = new QpackTableSync(decoder, maxBlockedStreams: 10);

        // Encode two different header blocks
        var headers1 = new List<(string, string)> { ("x-stream-a", "val-a") };
        var headers2 = new List<(string, string)> { ("x-stream-b", "val-b") };

        var encoded1 = encoder.Encode(headers1);
        var instructions1 = encoder.EncoderInstructions.ToArray();

        var encoded2 = encoder.Encode(headers2);
        var instructions2 = encoder.EncoderInstructions.ToArray();

        // Block both streams (no sync)
        var result1 = sync.TryDecodeOrBlock(encoded1, streamId: 4);
        var result2 = sync.TryDecodeOrBlock(encoded2, streamId: 8);

        Assert.True(result1.IsBlocked);
        Assert.True(result2.IsBlocked);
        Assert.Equal(2, sync.BlockedStreamCount);

        // Apply all encoder instructions
        sync.ApplyEncoderInstructions(instructions1);
        sync.ApplyEncoderInstructions(instructions2);

        // Resolve — both should unblock
        var resolved = sync.ResolveBlockedStreams();

        Assert.Equal(2, resolved.Count);
        Assert.Equal(0, sync.BlockedStreamCount);

        // Verify stream IDs and content
        var streamIds = new HashSet<int> { resolved[0].StreamId, resolved[1].StreamId };
        Assert.Contains(4, streamIds);
        Assert.Contains(8, streamIds);
    }


    /// RFC 9204 §4.4.3 — Insert Count Increment updates Known Received Count
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.4.3")]
    public void Should_UpdateKnownReceivedCount_ViaInsertCountIncrement()
    {
        var decoder = new QpackDecoder(maxTableCapacity: 4096);
        var sync = new QpackTableSync(decoder);

        // Manually insert entries into decoder's table (simulating encoder instructions)
        decoder.DynamicTable.Insert("x-test", "value1");
        decoder.DynamicTable.Insert("x-test", "value2");

        Assert.Equal(0, sync.KnownReceivedCount);

        // Write an Insert Count Increment instruction
        Span<byte> buf = new byte[16];
        var span = buf;
        var increment = sync.WriteInsertCountIncrement(ref span);
        var written = buf.Length - span.Length;

        Assert.Equal(2, increment);
        Assert.Equal(2, sync.KnownReceivedCount);

        // Process the instruction on the encoder side
        var encoderSync = new QpackTableSync(new QpackDecoder(maxTableCapacity: 4096));
        encoderSync.ProcessDecoderInstructions(buf[..written]);

        Assert.Equal(2, encoderSync.KnownReceivedCount);
    }

    /// RFC 9204 §4.4.2 — Stream Cancellation removes blocked stream
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.4.2")]
    public void Should_RemoveBlockedStream_OnStreamCancellation()
    {
        var encoder = new QpackEncoder(maxTableCapacity: 4096);
        var decoder = new QpackDecoder(maxTableCapacity: 4096, maxBlockedStreams: 10);
        var sync = new QpackTableSync(decoder, maxBlockedStreams: 10);

        var headers = new List<(string, string)> { ("x-cancel-me", "will-cancel") };
        var encoded = encoder.Encode(headers);

        // Block stream 12
        var result = sync.TryDecodeOrBlock(encoded, streamId: 12);
        Assert.True(result.IsBlocked);
        Assert.Equal(1, sync.BlockedStreamCount);

        // Write and process a Stream Cancellation instruction for stream 12
        var cancelBuf = new byte[16];
        Span<byte> cancelSpan = cancelBuf;
        var n = QpackDecoderInstructionWriter.WriteStreamCancellation(12, ref cancelSpan);
        sync.ProcessDecoderInstructions(cancelBuf.AsSpan(0, n));

        Assert.Equal(0, sync.BlockedStreamCount);
    }
}
