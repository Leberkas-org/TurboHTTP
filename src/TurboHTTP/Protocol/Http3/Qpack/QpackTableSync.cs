namespace TurboHTTP.Protocol.Http3.Qpack;

/// <summary>
/// RFC 9204 §2.1.1, §2.1.2 — QPACK Table Synchronization Coordinator.
///
/// Single owner of both <see cref="QpackEncoder"/> and <see cref="QpackDecoder"/>.
/// Coordinates both directions of QPACK dynamic table synchronization:
///
///   1. **Peer encoder → our decoder** (§4.3): Encoder instructions from the peer
///      (inserts, capacity changes, duplicates) are applied to the decoder's dynamic
///      table via <see cref="ApplyEncoderInstructions"/>.
///
///   2. **Peer decoder → our encoder** (§4.4): Decoder instructions from the peer
///      (Section Acknowledgment, Insert Count Increment, Stream Cancellation) are
///      forwarded to <see cref="QpackEncoder.ApplyDecoderInstruction"/> via
///      <see cref="ProcessDecoderInstructions"/> so the encoder's Known Received
///      Count stays accurate.
///
///   3. **Our decoder → peer encoder** (§4.4.3): Generates Insert Count Increment
///      instructions via <see cref="WriteInsertCountIncrement"/> to inform the peer's
///      encoder how far our decoder has progressed.
///
///   4. **Blocked stream resolution** (§2.1.2): When a header block's Required Insert
///      Count exceeds the decoder's current insert count, the stream is blocked.
///      <see cref="ResolveBlockedStreams"/> unblocks them as encoder instructions arrive.
/// </summary>
internal sealed class QpackTableSync
{
    private readonly QpackInstructionDecoder _instructionDecoder;
    private readonly List<BlockedStream> _blockedStreams = [];
    private readonly int _maxBlockedStreams;
    private readonly int _encoderMaxCapacity;
    private readonly int _decoderMaxCapacity;

    /// <summary>
    /// Creates a new QPACK table synchronization coordinator that owns both encoder and decoder.
    /// </summary>
    /// <param name="encoderMaxCapacity">
    /// Maximum QPACK dynamic table capacity for the encoder (SETTINGS_QPACK_MAX_TABLE_CAPACITY).
    /// RFC 9204 §3.2.3: set to 0 until peer SETTINGS is received to disable dynamic table.
    /// </param>
    /// <param name="decoderMaxCapacity">
    /// Maximum QPACK dynamic table capacity for the decoder.
    /// </param>
    /// <param name="maxBlockedStreams">
    /// Maximum number of streams that may be blocked waiting for dynamic table updates
    /// (SETTINGS_QPACK_BLOCKED_STREAMS).
    /// </param>
    public QpackTableSync(int encoderMaxCapacity = 0, int decoderMaxCapacity = 4096, int maxBlockedStreams = 100)
    {
        if (maxBlockedStreams < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBlockedStreams),
                "Max blocked streams must be non-negative.");
        }

        _encoderMaxCapacity = encoderMaxCapacity;
        _decoderMaxCapacity = decoderMaxCapacity;
        _maxBlockedStreams = maxBlockedStreams;
        Encoder = new QpackEncoder(encoderMaxCapacity);
        Decoder = new QpackDecoder(decoderMaxCapacity, maxBlockedStreams);
        _instructionDecoder = new QpackInstructionDecoder();
    }

    /// <summary>The QPACK encoder (for request encoding and encoder instruction access).</summary>
    public QpackEncoder Encoder { get; private set; }

    /// <summary>The QPACK decoder (for response decoding and decoder instruction access).</summary>
    public QpackDecoder Decoder { get; private set; }

    /// <summary>
    /// RFC 9204 §2.1.1 — The Known Received Count: the largest value of Insert Count
    /// that the encoder knows the decoder has received.
    /// </summary>
    public int KnownReceivedCount { get; private set; }

    /// <summary>Current number of blocked streams.</summary>
    public int BlockedStreamCount => _blockedStreams.Count;

    /// <summary>Current insert count of the decoder's dynamic table.</summary>
    public int InsertCount => Decoder.DynamicTable.InsertCount;

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
    /// RFC 9204 §4.4 — Processes inbound decoder instructions from the peer.
    ///
    /// The peer's decoder sends these instructions to inform our encoder about
    /// its dynamic table state: Section Acknowledgment (§4.4.1), Insert Count
    /// Increment (§4.4.3), and Stream Cancellation (§4.4.2). Each instruction
    /// is forwarded to <see cref="QpackEncoder.ApplyDecoderInstruction"/> so the
    /// encoder's Known Received Count stays accurate.
    ///
    /// Stream Cancellation additionally removes any blocked response streams for
    /// that stream ID, since the peer will not process the request.
    /// </summary>
    /// <param name="data">Raw decoder instruction stream bytes.</param>
    /// <returns>The number of instructions processed.</returns>
    public int ProcessDecoderInstructions(ReadOnlySpan<byte> data)
    {
        var instrDecoder = new QpackInstructionDecoder();
        var instructions = instrDecoder.DecodeAllDecoderInstructions(data);

        foreach (var instruction in instructions)
        {
            Encoder.ApplyDecoderInstruction(instruction);

            if (instruction.Type == DecoderInstructionType.StreamCancellation)
            {
                _blockedStreams.RemoveAll(b => b.StreamId == instruction.IntValue);
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
        var result = Decoder.TryDecode(data.Span, streamId);

        if (result.IsBlocked)
        {
            if (_blockedStreams.Count >= _maxBlockedStreams)
            {
                throw new QpackException(
                    $"RFC 9204 §2.1.2 violation: Maximum blocked streams ({_maxBlockedStreams}) exceeded.");
            }

            _blockedStreams.Add(new BlockedStream(streamId, result.RequiredInsertCount, data));
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
            if (blocked.RequiredInsertCount <= Decoder.DynamicTable.InsertCount)
            {
                // Stream can now be decoded
                var headers = Decoder.Decode(blocked.Data.Span, blocked.StreamId);
                resolved.Add((blocked.StreamId, headers));
            }
            else
            {
                remaining.Add(blocked);
            }
        }

        _blockedStreams.Clear();
        _blockedStreams.AddRange(remaining);

        // Update the decoder's blocked stream count
        Decoder.UnblockStreams();

        return resolved;
    }

    /// <summary>
    /// Generates an Insert Count Increment decoder instruction for the current state.
    /// Call this to inform the encoder that the decoder has received all inserts up to now.
    /// </summary>
    /// <param name="output">Destination span (sliced on return to exclude written bytes).</param>
    /// <returns>The increment value written, or 0 if no increment was needed.</returns>
    public int WriteInsertCountIncrement(ref Span<byte> output)
    {
        var currentInsertCount = Decoder.DynamicTable.InsertCount;
        var increment = currentInsertCount - KnownReceivedCount;

        if (increment > 0)
        {
            QpackDecoderInstructionWriter.WriteInsertCountIncrement(increment, ref output);
            KnownReceivedCount = currentInsertCount;
            return increment;
        }

        return 0;
    }


    /// <summary>
    /// Resets all QPACK state for reconnection. Creates fresh encoder and decoder
    /// instances so both sides start clean (RFC 9204 §3.2.3).
    /// </summary>
    public void Reset()
    {
        Encoder = new QpackEncoder(_encoderMaxCapacity);
        Decoder = new QpackDecoder(_decoderMaxCapacity, _maxBlockedStreams);
        KnownReceivedCount = 0;
        _blockedStreams.Clear();
    }

    private void ApplyEncoderInstruction(EncoderInstruction instruction)
    {
        switch (instruction.Type)
        {
            case EncoderInstructionType.InsertWithNameReference:
                {
                    var name = instruction.IsStatic
                        ? QpackStaticTable.Entries[instruction.NameIndex].Name
                        : Decoder.DynamicTable.GetEntry(
                            Decoder.DynamicTable.InsertCount - 1 - instruction.NameIndex)!.Value.Name;
                    Decoder.DynamicTable.Insert(name, instruction.ValueString);
                    break;
                }

            case EncoderInstructionType.InsertWithLiteralName:
                Decoder.DynamicTable.Insert(instruction.NameString, instruction.ValueString);
                break;

            case EncoderInstructionType.SetDynamicTableCapacity:
                Decoder.DynamicTable.SetCapacity(instruction.IntValue);
                break;

            case EncoderInstructionType.Duplicate:
                Decoder.DynamicTable.Duplicate(instruction.IntValue);
                break;
        }
    }
}