using System.Buffers;

namespace TurboHTTP.Protocol.Syntax.Http3.Qpack;

/// <summary>
/// RFC 9204 §4.5 — QPACK Header Block Decoder.
///
/// Decodes a QPACK-encoded header block back into a list of header fields.
/// The decoder uses both the static table (Appendix A) and a dynamic table
/// that must be kept synchronised with the encoder's table via instruction streams.
///
/// Key responsibilities:
///   - Decodes the header block prefix (Required Insert Count + Base)
///   - Decodes all 5 header field representations (§4.5.2–§4.5.6)
///   - Validates Required Insert Count against the decoder's known insert count
///   - Tracks blocked streams when Required Insert Count exceeds known insert count
///   - Emits decoder instructions (Section Acknowledgement) as a side effect
///
/// Design:
///   - Stateful: maintains a dynamic table and blocked stream state
///   - Huffman decoding is handled transparently by <see cref="QpackStringCodec"/>
///   - Decoder instructions are collected in <see cref="DecoderInstructions"/>
/// </summary>
internal sealed class QpackDecoder
{
    private readonly int _maxTableCapacity;
    private readonly int _maxBlockedStreams;
    private IMemoryOwner<byte>? _instructionOwner;
    private int _instructionBytesWritten;

    // Reused per-Decode/TryDecode-call header list. Cleared at the start of each call.
    // Safe to reuse: QPACK processes one header block at a time per connection; Akka back-pressure
    // guarantees the list is consumed before the next Decode/TryDecode call.
    private readonly List<(string Name, string Value)> _headers = [];

    /// <summary>
    /// Creates a new QPACK decoder.
    /// </summary>
    /// <param name="maxTableCapacity">
    /// Maximum dynamic table capacity in bytes (SETTINGS_QPACK_MAX_TABLE_CAPACITY).
    /// Set to 0 to disable dynamic table usage.
    /// </param>
    /// <param name="maxBlockedStreams">
    /// Maximum number of streams that may be blocked waiting for dynamic table updates
    /// (SETTINGS_QPACK_BLOCKED_STREAMS). Default 0 means no blocking allowed.
    /// </param>
    public QpackDecoder(int maxTableCapacity = 4096, int maxBlockedStreams = 100)
    {
        if (maxTableCapacity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxTableCapacity), "Capacity must be non-negative.");
        }

        if (maxBlockedStreams < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBlockedStreams),
                "Max blocked streams must be non-negative.");
        }

        _maxTableCapacity = maxTableCapacity;
        DynamicTable = new QpackDynamicTable(maxTableCapacity);
        _maxBlockedStreams = maxBlockedStreams;
    }

    /// <summary>The decoder's dynamic table (for inspection and testing).</summary>
    public QpackDynamicTable DynamicTable { get; }

    /// <summary>
    /// Decoder instructions emitted during the most recent <see cref="Decode"/> call.
    /// These must be sent on the decoder instruction stream after processing the header block.
    /// Typically contains a Section Acknowledgement (§4.4.1).
    /// </summary>
    public ReadOnlyMemory<byte> DecoderInstructions =>
        _instructionOwner?.Memory[.._instructionBytesWritten] ?? ReadOnlyMemory<byte>.Empty;

    /// <summary>Current number of blocked streams.</summary>
    public int BlockedStreamCount { get; private set; }

    /// <summary>
    /// Decodes a QPACK header block into a list of header fields.
    /// After calling this method, check <see cref="DecoderInstructions"/> for
    /// any decoder instructions that must be sent on the decoder stream.
    /// </summary>
    /// <param name="data">The encoded header block bytes.</param>
    /// <param name="streamId">
    /// The stream ID this header block belongs to (used for Section Acknowledgment).
    /// </param>
    /// <returns>The decoded list of header fields as (name, value) pairs.</returns>
    public IReadOnlyList<(string Name, string Value)> Decode(ReadOnlySpan<byte> data, int streamId = 0)
    {
        EnsureInstructionBuffer(64);
        _instructionBytesWritten = 0;

        var pos = 0;

        // Phase 1: Decode the header block prefix (§4.5.1)
        var (requiredInsertCount, encodingBase) = DecodePrefix(data, ref pos);

        // Phase 2: Validate Required Insert Count against known state
        ValidateRequiredInsertCount(requiredInsertCount);

        // Phase 3: Decode each header field representation
        _headers.Clear();

        while (pos < data.Length)
        {
            var header = DecodeHeaderField(data, ref pos, encodingBase);
            _headers.Add(header);
        }

        // Phase 4: Emit Section Acknowledgement if dynamic table was referenced
        if (requiredInsertCount > 0)
        {
            var w = SpanWriter.Create(_instructionOwner!.Memory.Span[_instructionBytesWritten..]);
            _instructionBytesWritten +=
                QpackDecoderInstructionWriter.WriteSectionAcknowledgment(streamId, ref w);
        }

        return _headers;
    }

    /// <summary>
    /// Attempts to decode a header block, returning a <see cref="QpackDecodeResult"/>
    /// that indicates whether the stream is blocked waiting for dynamic table updates.
    /// </summary>
    /// <param name="data">The encoded header block bytes.</param>
    /// <param name="streamId">The stream ID this header block belongs to.</param>
    /// <returns>A result containing the decoded headers or blocked status.</returns>
    public QpackDecodeResult TryDecode(ReadOnlySpan<byte> data, int streamId = 0)
    {
        EnsureInstructionBuffer(64);
        _instructionBytesWritten = 0;

        var pos = 0;
        var (requiredInsertCount, encodingBase) = DecodePrefix(data, ref pos);

        // Check if the stream would be blocked
        if (requiredInsertCount > DynamicTable.InsertCount)
        {
            if (BlockedStreamCount >= _maxBlockedStreams)
            {
                throw new QpackException(
                    $"RFC 9204 §2.1.2 violation: Blocked stream limit reached ({_maxBlockedStreams}). " +
                    $"Required Insert Count {requiredInsertCount} exceeds known {DynamicTable.InsertCount}.");
            }

            BlockedStreamCount++;
            return QpackDecodeResult.Blocked(requiredInsertCount);
        }

        _headers.Clear();

        while (pos < data.Length)
        {
            var header = DecodeHeaderField(data, ref pos, encodingBase);
            _headers.Add(header);
        }

        if (requiredInsertCount > 0)
        {
            var w = SpanWriter.Create(_instructionOwner!.Memory.Span[_instructionBytesWritten..]);
            _instructionBytesWritten +=
                QpackDecoderInstructionWriter.WriteSectionAcknowledgment(streamId, ref w);
        }

        return QpackDecodeResult.Success(_headers);
    }

    public void ApplyEncoderInstruction(EncoderInstruction instruction)
    {
        switch (instruction.Type)
        {
            case EncoderInstructionType.InsertWithNameReference:
                {
                    var name = instruction.IsStatic
                        ? QpackStaticTable.Entries[instruction.NameIndex].Name
                        : DynamicTable.GetEntry(DynamicTable.InsertCount - 1 - instruction.NameIndex)!.Value.Name;
                    DynamicTable.Insert(name, instruction.Value);
                    break;
                }

            case EncoderInstructionType.InsertWithLiteralName:
                DynamicTable.Insert(instruction.Name, instruction.Value);
                break;

            case EncoderInstructionType.SetDynamicTableCapacity:
                DynamicTable.SetCapacity(instruction.IntValue);
                break;

            case EncoderInstructionType.Duplicate:
                DynamicTable.Duplicate(instruction.IntValue);
                break;
        }
    }

    /// <summary>
    /// Notifies the decoder that previously blocked streams may now proceed
    /// because the encoder has inserted entries up to the given insert count.
    /// Call this after processing encoder instructions that insert entries.
    /// </summary>
    public void UnblockStreams()
    {
        BlockedStreamCount = 0;
    }

    private void EnsureInstructionBuffer(int minCapacity)
    {
        if (_instructionOwner != null && _instructionOwner.Memory.Length >= minCapacity)
        {
            return;
        }

        _instructionOwner?.Dispose();
        _instructionOwner = MemoryPool<byte>.Shared.Rent(minCapacity);
    }

    /// <summary>
    /// RFC 9204 §4.5.1 — Decodes the header block prefix.
    ///
    ///   0   1   2   3   4   5   6   7
    /// +---+---+---+---+---+---+---+---+
    /// |   Required Insert Count (8+)  |
    /// +---+---------------------------+
    /// | S |      Delta Base (7+)      |
    /// +---+---------------------------+
    /// </summary>
    private (int RequiredInsertCount, int Base) DecodePrefix(ReadOnlySpan<byte> data, ref int pos)
    {
        if (pos + 2 > data.Length)
        {
            throw new QpackException("RFC 9204 §4.5.1 violation: Header block too short for prefix.");
        }

        // Decode Encoded Required Insert Count (8-bit prefix)
        var encodedRic = QpackIntegerCodec.Decode(data, ref pos, 8);

        // Decode Sign bit + Delta Base (7-bit prefix)
        var signBit = (data[pos] & 0x80) != 0;
        var deltaBase = QpackIntegerCodec.Decode(data, ref pos, 7);

        // Decode the Required Insert Count (§4.5.1.1)
        var requiredInsertCount = DecodeRequiredInsertCount(encodedRic);

        // Compute Base (§4.5.1.2)
        int encodingBase;
        if (requiredInsertCount == 0)
        {
            encodingBase = 0;
        }
        else if (!signBit)
        {
            // S=0: Base = RequiredInsertCount + DeltaBase
            encodingBase = requiredInsertCount + deltaBase;
        }
        else
        {
            // S=1: Base = RequiredInsertCount - DeltaBase - 1
            encodingBase = requiredInsertCount - deltaBase - 1;
        }

        return (requiredInsertCount, encodingBase);
    }

    /// <summary>
    /// RFC 9204 §4.5.1.1 — Decodes the Required Insert Count from its encoded form.
    ///
    /// If EncodedInsertCount is 0, RequiredInsertCount is 0.
    /// Otherwise: RequiredInsertCount = decoded from modular arithmetic using MaxEntries.
    /// </summary>
    private int DecodeRequiredInsertCount(int encodedInsertCount)
    {
        if (encodedInsertCount == 0)
        {
            return 0;
        }

        var maxEntries = _maxTableCapacity / QpackDynamicTable.EntryOverhead;

        if (maxEntries == 0)
        {
            throw new QpackException(
                "RFC 9204 §4.5.1.1 violation: Encoded Required Insert Count > 0 but MaxEntries is 0.");
        }

        var fullRange = 2 * maxEntries;

        if (encodedInsertCount > fullRange)
        {
            throw new QpackException(
                $"RFC 9204 §4.5.1.1 violation: Encoded Required Insert Count {encodedInsertCount} exceeds FullRange {fullRange}.");
        }

        // RFC 9204 §4.5.1.1 decoding algorithm
        var totalNumberOfInserts = DynamicTable.InsertCount;
        var maxValue = totalNumberOfInserts + maxEntries;
        var maxWrapped = maxValue / fullRange * fullRange;
        var requiredInsertCount = maxWrapped + encodedInsertCount - 1;

        // If requiredInsertCount exceeds maxValue, an encoder's value of 0 is decoded
        if (requiredInsertCount > maxValue)
        {
            if (requiredInsertCount <= fullRange)
            {
                throw new QpackException(
                    "RFC 9204 §4.5.1.1 violation: Invalid Required Insert Count.");
            }

            requiredInsertCount -= fullRange;
        }

        // Final validation: must be > 0
        if (requiredInsertCount <= 0)
        {
            throw new QpackException(
                "RFC 9204 §4.5.1.1 violation: Decoded Required Insert Count must be positive when encoded value is non-zero.");
        }

        return requiredInsertCount;
    }

    private void ValidateRequiredInsertCount(int requiredInsertCount)
    {
        if (requiredInsertCount > DynamicTable.InsertCount)
        {
            throw new QpackException(
                $"RFC 9204 §4.5.1.1 violation: Required Insert Count {requiredInsertCount} " +
                $"exceeds known Insert Count {DynamicTable.InsertCount}. Stream should be blocked.");
        }
    }

    private (string Name, string Value) DecodeHeaderField(ReadOnlySpan<byte> data, ref int pos, int encodingBase)
    {
        if (pos >= data.Length)
        {
            throw new QpackException("RFC 9204 §4.5 violation: Unexpected end of header block.");
        }

        var firstByte = data[pos];

        if ((firstByte & 0x80) != 0)
        {
            // §4.5.2 — Indexed Header Field: 1Txxxxxx
            return DecodeIndexedHeaderField(data, ref pos, encodingBase);
        }

        if ((firstByte & 0xC0) == 0x40)
        {
            // §4.5.4 — Literal Header Field with Name Reference: 01NTxxxx
            return DecodeLiteralWithNameReference(data, ref pos, encodingBase);
        }

        if ((firstByte & 0xE0) == 0x20)
        {
            // §4.5.6 — Literal Header Field without Name Reference: 001NHxxx
            return DecodeLiteralWithoutNameReference(data, ref pos);
        }

        return (firstByte & 0xF0) switch
        {
            0x10 => DecodePostBaseIndexed(data, ref pos, encodingBase),
            0x00 => DecodeLiteralWithPostBaseNameReference(data, ref pos, encodingBase),
            _ => throw new QpackException($"RFC 9204 §4.5 violation: Unknown header field pattern 0x{firstByte:X2}.")
        };
    }

    /// <summary>
    /// §4.5.2 — Indexed Header Field.
    ///
    ///   0   1   2   3   4   5   6   7
    /// +---+---+---+---+---+---+---+---+
    /// | 1 | T |      Index (6+)       |
    /// +---+---+-----------------------+
    ///
    /// T=1: static table reference
    /// T=0: dynamic table reference (relative index = Base - AbsoluteIndex - 1)
    /// </summary>
    private (string Name, string Value) DecodeIndexedHeaderField(ReadOnlySpan<byte> data, ref int pos, int encodingBase)
    {
        var isStatic = (data[pos] & 0x40) != 0;
        var index = QpackIntegerCodec.Decode(data, ref pos, 6);

        if (isStatic)
        {
            return LookupStaticEntry(index);
        }

        // Dynamic: relative index → absolute index
        var absoluteIndex = encodingBase - index - 1;
        return LookupDynamicEntry(absoluteIndex);
    }

    /// <summary>
    /// §4.5.3 — Indexed Header Field with Post-Base Index.
    ///
    ///   0   1   2   3   4   5   6   7
    /// +---+---+---+---+---+---+---+---+
    /// | 0 | 0 | 0 | 1 |  Index (4+)   |
    /// +---+---+---+---+---------------+
    ///
    /// Absolute index = Base + PostBaseIndex
    /// </summary>
    private (string Name, string Value) DecodePostBaseIndexed(ReadOnlySpan<byte> data, ref int pos, int encodingBase)
    {
        var postBaseIndex = QpackIntegerCodec.Decode(data, ref pos, 4);
        var absoluteIndex = encodingBase + postBaseIndex;
        return LookupDynamicEntry(absoluteIndex);
    }

    /// <summary>
    /// §4.5.4 — Literal Header Field with Name Reference.
    ///
    ///   0   1   2   3   4   5   6   7
    /// +---+---+---+---+---+---+---+---+
    /// | 0 | 1 | N | T |NameIndex (4+) |
    /// +---+---+---+---+---------------+
    /// | H |     Value Length (7+)      |
    /// +---+---------------------------+
    /// |  Value String (Length bytes)   |
    /// +-------------------------------+
    ///
    /// T=1: static table name reference
    /// T=0: dynamic table name reference (relative index)
    /// N=1: never-indexed (intermediaries must not re-index)
    /// </summary>
    private (string Name, string Value) DecodeLiteralWithNameReference(ReadOnlySpan<byte> data, ref int pos,
        int encodingBase)
    {
        var isStatic = (data[pos] & 0x10) != 0;
        // N bit at 0x20 — we read it but don't need it for decoding
        var index = QpackIntegerCodec.Decode(data, ref pos, 4);

        string name;
        if (isStatic)
        {
            if (index is < 0 or >= QpackStaticTable.Count)
            {
                throw new QpackException($"RFC 9204 §4.5.4 violation: Static table index {index} out of range.");
            }

            name = QpackStaticTable.Entries[index].Name;
        }
        else
        {
            var absoluteIndex = encodingBase - index - 1;
            var entry = DynamicTable.GetEntry(absoluteIndex);
            if (entry is null)
            {
                throw new QpackException(
                    $"RFC 9204 §4.5.4 violation: Dynamic table entry at absolute index {absoluteIndex} not found (relative {index}, base {encodingBase}).");
            }

            name = entry.Value.Name;
        }

        var value = QpackStringCodec.DecodeToString(data, ref pos, 7);

        return (name, value);
    }

    /// <summary>
    /// §4.5.5 — Literal Header Field with Post-Base Name Reference.
    ///
    ///   0   1   2   3   4   5   6   7
    /// +---+---+---+---+---+---+---+---+
    /// | 0 | 0 | 0 | 0 | N |NameIdx(3+)|
    /// +---+---+---+---+---+-----------+
    /// | H |     Value Length (7+)      |
    /// +---+---------------------------+
    /// |  Value String (Length bytes)   |
    /// +-------------------------------+
    ///
    /// Absolute name index = Base + PostBaseIndex
    /// </summary>
    private (string Name, string Value) DecodeLiteralWithPostBaseNameReference(ReadOnlySpan<byte> data, ref int pos,
        int encodingBase)
    {
        // N bit at 0x08 — read but not needed for decoding
        var postBaseIndex = QpackIntegerCodec.Decode(data, ref pos, 3);
        var absoluteIndex = encodingBase + postBaseIndex;

        var entry = DynamicTable.GetEntry(absoluteIndex);
        if (entry is null)
        {
            throw new QpackException(
                $"RFC 9204 §4.5.5 violation: Dynamic table entry at absolute index {absoluteIndex} not found (post-base {postBaseIndex}, base {encodingBase}).");
        }

        var name = entry.Value.Name;
        var value = QpackStringCodec.DecodeToString(data, ref pos, 7);

        return (name, value);
    }

    /// <summary>
    /// §4.5.6 — Literal Header Field without Name Reference.
    ///
    ///   0   1   2   3   4   5   6   7
    /// +---+---+---+---+---+---+---+---+
    /// | 0 | 0 | 1 | N | H |NameLen(3+)|
    /// +---+---+---+---+---+-----------+
    /// |  Name String (Length bytes)    |
    /// +---+---------------------------+
    /// | H |     Value Length (7+)      |
    /// +---+---------------------------+
    /// |  Value String (Length bytes)   |
    /// +-------------------------------+
    /// </summary>
    private static (string Name, string Value) DecodeLiteralWithoutNameReference(ReadOnlySpan<byte> data, ref int pos)
    {
        // N bit at 0x10 — read but not needed for decoding
        // H bit and name length are decoded by QpackStringCodec (3-bit prefix)
        var name = QpackStringCodec.DecodeToString(data, ref pos, 3);
        var value = QpackStringCodec.DecodeToString(data, ref pos, 7);

        return (name, value);
    }

    private static (string Name, string Value) LookupStaticEntry(int index)
    {
        if (index is < 0 or >= QpackStaticTable.Count)
        {
            throw new QpackException(
                $"RFC 9204 §3.1 violation: Static table index {index} out of range (0–{QpackStaticTable.Count - 1}).");
        }

        return QpackStaticTable.Entries[index];
    }

    private (string Name, string Value) LookupDynamicEntry(int absoluteIndex)
    {
        var entry = DynamicTable.GetEntry(absoluteIndex);
        if (entry is null)
        {
            throw new QpackException(
                $"RFC 9204 §3.2 violation: Dynamic table entry at absolute index {absoluteIndex} not found " +
                $"(InsertCount={DynamicTable.InsertCount}, DroppedCount={DynamicTable.DroppedCount}).");
        }

        return (entry.Value.Name, entry.Value.Value);
    }
}
