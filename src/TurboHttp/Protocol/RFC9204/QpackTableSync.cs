using System.Buffers;

namespace TurboHttp.Protocol.RFC9204;

/// <summary>
/// Represents a blocked stream waiting for dynamic table updates.
/// </summary>
public sealed class BlockedStream
{
    /// <summary>The stream ID that is blocked.</summary>
    public int StreamId { get; }

    /// <summary>The Required Insert Count that must be reached to unblock.</summary>
    public int RequiredInsertCount { get; }

    /// <summary>The raw header block data to decode once unblocked.</summary>
    public ReadOnlyMemory<byte> Data { get; }

    public BlockedStream(int streamId, int requiredInsertCount, ReadOnlyMemory<byte> data)
    {
        StreamId = streamId;
        RequiredInsertCount = requiredInsertCount;
        Data = data;
    }
}

/// <summary>
/// RFC 9204 §2.1.1, §2.1.2 — QPACK Table Synchronization Coordinator.
///
/// Coordinates dynamic table state between encoder and decoder via instruction streams.
/// Manages three synchronization concerns:
///
///   1. **Encoder → Decoder** (§4.3): Encoder instructions (inserts, capacity changes,
///      duplicates) are applied to the decoder's dynamic table to keep it in sync.
///
///   2. **Decoder → Encoder** (§4.4): Decoder instructions (Section Acknowledgment,
///      Insert Count Increment, Stream Cancellation) inform the encoder about decoder state.
///      The encoder tracks "Known Received Count" (KRC) — the largest insert count it knows
///      the decoder has received.
///
///   3. **Blocked stream resolution** (§2.1.2): When a header block's Required Insert Count
///      exceeds the decoder's current insert count, the stream is blocked. As encoder
///      instructions arrive and the insert count grows, blocked streams whose required
///      insert count is reached are automatically resolved.
///
/// Usage:
///   - Call <see cref="ApplyEncoderInstructions"/> with bytes from the encoder instruction stream.
///   - Call <see cref="ProcessDecoderInstructions"/> with bytes from the decoder instruction stream.
///   - Call <see cref="TryDecodeOrBlock"/> to decode header blocks, handling blocked streams.
///   - Check <see cref="ResolveBlockedStreams"/> after applying encoder instructions to unblock streams.
/// </summary>
public sealed class QpackTableSync
{
    private readonly QpackDecoder _decoder;
    private readonly QpackInstructionDecoder _instructionDecoder;
    private readonly List<BlockedStream> _blockedStreams = [];
    private readonly int _maxBlockedStreams;

    // Encoder-side state: tracks what the encoder knows the decoder has received

    // Tracks the highest Required Insert Count seen in Section Acknowledgments per stream
    private readonly Dictionary<int, int> _streamMaxInsertCounts = [];

    /// <summary>
    /// Creates a new QPACK table synchronization coordinator.
    /// </summary>
    /// <param name="decoder">The QPACK decoder whose dynamic table will be updated.</param>
    /// <param name="maxBlockedStreams">
    /// Maximum number of streams that may be blocked waiting for dynamic table updates
    /// (SETTINGS_QPACK_BLOCKED_STREAMS).
    /// </param>
    public QpackTableSync(QpackDecoder decoder, int maxBlockedStreams = 100)
    {
        ArgumentNullException.ThrowIfNull(decoder);

        if (maxBlockedStreams < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBlockedStreams), "Max blocked streams must be non-negative.");
        }

        _decoder = decoder;
        _maxBlockedStreams = maxBlockedStreams;
        _instructionDecoder = new QpackInstructionDecoder();
    }

    /// <summary>The decoder's dynamic table (for inspection and testing).</summary>
    public QpackDynamicTable DynamicTable => _decoder.DynamicTable;

    /// <summary>
    /// RFC 9204 §2.1.1 — The Known Received Count: the largest value of Insert Count
    /// that the encoder knows the decoder has received.
    /// </summary>
    public int KnownReceivedCount { get; private set; }

    /// <summary>Current number of blocked streams.</summary>
    public int BlockedStreamCount => _blockedStreams.Count;

    /// <summary>Current insert count of the decoder's dynamic table.</summary>
    public int InsertCount => _decoder.DynamicTable.InsertCount;

    /// <summary>
    /// RFC 9204 §4.3 — Applies encoder instructions to the decoder's dynamic table.
    ///
    /// Processes all complete encoder instructions from the provided data, updating
    /// the decoder's dynamic table accordingly. Partial trailing data is buffered
    /// internally for the next call.
    /// </summary>
    /// <param name="data">Raw encoder instruction stream bytes.</param>
    /// <returns>The number of instructions applied.</returns>
    public int ApplyEncoderInstructions(ReadOnlySpan<byte> data)
    {
        var instructions = _instructionDecoder.DecodeAllEncoderInstructions(data);

        foreach (var instruction in instructions)
        {
            ApplyEncoderInstruction(instruction);
        }

        return instructions.Length;
    }

    /// <summary>
    /// RFC 9204 §4.4 — Processes decoder instructions to update encoder-side state.
    ///
    /// Handles Section Acknowledgment (§4.4.1), Stream Cancellation (§4.4.2),
    /// and Insert Count Increment (§4.4.3) instructions.
    /// </summary>
    /// <param name="data">Raw decoder instruction stream bytes.</param>
    /// <returns>The number of instructions processed.</returns>
    public int ProcessDecoderInstructions(ReadOnlySpan<byte> data)
    {
        var instrDecoder = new QpackInstructionDecoder();
        var instructions = instrDecoder.DecodeAllDecoderInstructions(data);

        foreach (var instruction in instructions)
        {
            switch (instruction.Type)
            {
                case DecoderInstructionType.SectionAcknowledgment:
                    ProcessSectionAcknowledgment(instruction.IntValue);
                    break;

                case DecoderInstructionType.InsertCountIncrement:
                    ProcessInsertCountIncrement(instruction.IntValue);
                    break;

                case DecoderInstructionType.StreamCancellation:
                    ProcessStreamCancellation(instruction.IntValue);
                    break;
            }
        }

        return instructions.Length;
    }

    /// <summary>
    /// Attempts to decode a header block. If the Required Insert Count exceeds
    /// the decoder's current insert count, the stream is blocked and the data
    /// is queued for later resolution.
    /// </summary>
    /// <param name="data">The encoded header block bytes.</param>
    /// <param name="streamId">The stream ID this header block belongs to.</param>
    /// <returns>A decode result that may indicate blocking.</returns>
    public QpackDecodeResult TryDecodeOrBlock(ReadOnlyMemory<byte> data, int streamId)
    {
        var result = _decoder.TryDecode(data.Span, streamId);

        if (result.IsBlocked)
        {
            if (_blockedStreams.Count >= _maxBlockedStreams)
            {
                throw new QpackException(
                    $"RFC 9204 §2.1.2 violation: Maximum blocked streams ({_maxBlockedStreams}) exceeded.");
            }

            _blockedStreams.Add(new BlockedStream(streamId, result.RequiredInsertCount, data));
        }
        else
        {
            // Track the Required Insert Count for this stream for Section Ack handling
            TrackStreamInsertCount(streamId, _decoder.DynamicTable.InsertCount);
        }

        return result;
    }

    /// <summary>
    /// RFC 9204 §2.1.2 — Resolves blocked streams whose Required Insert Count
    /// has been reached by the decoder's current insert count.
    ///
    /// Call this after <see cref="ApplyEncoderInstructions"/> to unblock streams.
    /// </summary>
    /// <returns>
    /// A list of (streamId, headers) for each stream that was unblocked and decoded.
    /// </returns>
    public IReadOnlyList<(int StreamId, IReadOnlyList<(string Name, string Value)> Headers)> ResolveBlockedStreams()
    {
        var resolved = new List<(int, IReadOnlyList<(string Name, string Value)>)>();
        var remaining = new List<BlockedStream>();

        foreach (var blocked in _blockedStreams)
        {
            if (blocked.RequiredInsertCount <= _decoder.DynamicTable.InsertCount)
            {
                // Stream can now be decoded
                var headers = _decoder.Decode(blocked.Data.Span, blocked.StreamId);
                resolved.Add((blocked.StreamId, headers));
                TrackStreamInsertCount(blocked.StreamId, _decoder.DynamicTable.InsertCount);
            }
            else
            {
                remaining.Add(blocked);
            }
        }

        _blockedStreams.Clear();
        _blockedStreams.AddRange(remaining);

        // Update the decoder's blocked stream count
        _decoder.UnblockStreams();

        return resolved;
    }

    /// <summary>
    /// Generates an Insert Count Increment decoder instruction for the current state.
    /// Call this to inform the encoder that the decoder has received all inserts up to now.
    /// </summary>
    /// <param name="output">Destination buffer writer for the decoder instruction.</param>
    /// <returns>The increment value written, or 0 if no increment was needed.</returns>
    public int WriteInsertCountIncrement(IBufferWriter<byte> output)
    {
        var currentInsertCount = _decoder.DynamicTable.InsertCount;
        var increment = currentInsertCount - KnownReceivedCount;

        if (increment > 0)
        {
            QpackDecoderInstructionWriter.WriteInsertCountIncrement(increment, output);
            KnownReceivedCount = currentInsertCount;
            return increment;
        }

        return 0;
    }

    // ── Encoder instruction application ──────────────────────────────────

    private void ApplyEncoderInstruction(EncoderInstruction instruction)
    {
        switch (instruction.Type)
        {
            case EncoderInstructionType.InsertWithNameReference:
            {
                var name = instruction.IsStatic
                    ? QpackStaticTable.Entries[instruction.NameIndex].Name
                    : _decoder.DynamicTable.GetEntry(
                        _decoder.DynamicTable.InsertCount - 1 - instruction.NameIndex)!.Value.Name;
                _decoder.DynamicTable.Insert(name, instruction.ValueString);
                break;
            }

            case EncoderInstructionType.InsertWithLiteralName:
                _decoder.DynamicTable.Insert(instruction.NameString, instruction.ValueString);
                break;

            case EncoderInstructionType.SetDynamicTableCapacity:
                _decoder.DynamicTable.SetCapacity(instruction.IntValue);
                break;

            case EncoderInstructionType.Duplicate:
                _decoder.DynamicTable.Duplicate(instruction.IntValue);
                break;
        }
    }

    // ── Decoder instruction processing (encoder-side) ────────────────────

    /// <summary>
    /// RFC 9204 §4.4.1 — Process Section Acknowledgment.
    /// The encoder learns the decoder has processed a header block on this stream.
    /// KRC is updated to the highest Required Insert Count referenced by that stream.
    /// </summary>
    private void ProcessSectionAcknowledgment(int streamId)
    {
        if (_streamMaxInsertCounts.TryGetValue(streamId, out var maxInsertCount))
        {
            if (maxInsertCount > KnownReceivedCount)
            {
                KnownReceivedCount = maxInsertCount;
            }

            _streamMaxInsertCounts.Remove(streamId);
        }
    }

    /// <summary>
    /// RFC 9204 §4.4.3 — Process Insert Count Increment.
    /// The decoder explicitly tells the encoder it has received additional inserts.
    /// </summary>
    private void ProcessInsertCountIncrement(int increment)
    {
        if (increment <= 0)
        {
            throw new QpackException("RFC 9204 §4.4.3 violation: Insert Count Increment must be positive.");
        }

        KnownReceivedCount += increment;
    }

    /// <summary>
    /// RFC 9204 §4.4.2 — Process Stream Cancellation.
    /// Remove tracking state for the cancelled stream.
    /// </summary>
    private void ProcessStreamCancellation(int streamId)
    {
        _streamMaxInsertCounts.Remove(streamId);

        // Also remove any blocked stream entries for this stream
        _blockedStreams.RemoveAll(b => b.StreamId == streamId);
    }

    private void TrackStreamInsertCount(int streamId, int insertCount)
    {
        if (!_streamMaxInsertCounts.TryGetValue(streamId, out var current) || insertCount > current)
        {
            _streamMaxInsertCounts[streamId] = insertCount;
        }
    }
}
