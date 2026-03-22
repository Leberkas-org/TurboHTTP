using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;

namespace TurboHttp.Protocol.RFC9204;

/// <summary>
/// RFC 9204 §4.5 — QPACK Header Block Encoder.
///
/// Encodes a list of header fields into a QPACK header block representation.
/// The encoder uses both the static table (Appendix A) and a dynamic table,
/// emitting encoder instructions as a side effect when inserting new entries.
///
/// Key differences from HPACK (RFC 7541):
///   - Uses absolute indexing for the dynamic table (no head-of-line blocking)
///   - Header block prefix includes Required Insert Count and Base
///   - Five header field representations (§4.5.2–§4.5.6)
///   - Sensitive headers use the N (never-indexed) bit
///
/// Design:
///   - Writes into <see cref="IBufferWriter{T}"/> for zero-copy output
///   - Encoder instructions are collected in <see cref="EncoderInstructions"/>
///   - Huffman encoding auto-selects shorter representation (RFC 9204 §4.1.2)
///   - Sensitive headers (Authorization, Cookie, etc.) are automatically NEVERINDEX
/// </summary>
public sealed class QpackEncoder
{
    /// <summary>
    /// RFC 9204 §7.1 — Headers that MUST NOT be indexed by any intermediary.
    /// </summary>
    private static readonly HashSet<string> SensitiveHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "authorization",
        "proxy-authorization",
        "cookie",
        "set-cookie",
    };

    private readonly QpackDynamicTable _table;
    private readonly int _maxTableCapacity;
    private readonly bool _enableDynamicTable;
    private readonly ArrayBufferWriter<byte> _instructionBuffer = new(256);
    private readonly Dictionary<int, int> _pendingSections = new();
    private int _knownReceivedCount;

    /// <summary>
    /// Creates a new QPACK encoder.
    /// </summary>
    /// <param name="maxTableCapacity">
    /// Maximum dynamic table capacity in bytes (SETTINGS_QPACK_MAX_TABLE_CAPACITY).
    /// Set to 0 to disable dynamic table usage.
    /// </param>
    public QpackEncoder(int maxTableCapacity = 4096)
    {
        if (maxTableCapacity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxTableCapacity), "Capacity must be non-negative.");
        }

        _maxTableCapacity = maxTableCapacity;
        _table = new QpackDynamicTable(maxTableCapacity);
        _enableDynamicTable = maxTableCapacity > 0;
    }

    /// <summary>The encoder's dynamic table (for inspection and testing).</summary>
    public QpackDynamicTable DynamicTable => _table;

    /// <summary>
    /// Encoder instructions emitted during the most recent <see cref="Encode"/> call.
    /// These must be sent on the encoder instruction stream before the header block
    /// is transmitted on the request stream.
    /// </summary>
    public ReadOnlyMemory<byte> EncoderInstructions => _instructionBuffer.WrittenMemory;

    /// <summary>
    /// RFC 9204 §4.4 — The Known Received Count: the number of dynamic table inserts
    /// that the decoder has confirmed it has received (via Section Acknowledgment and
    /// Insert Count Increment instructions on the decoder stream).
    /// </summary>
    public int KnownReceivedCount => _knownReceivedCount;

    /// <summary>
    /// Records that a header block with the given Required Insert Count was sent on
    /// the specified stream. Must be called after <see cref="Encode"/> when the
    /// Required Insert Count is greater than zero, so that
    /// <see cref="ApplyDecoderInstruction"/> can process Section Acknowledgment.
    /// </summary>
    /// <param name="streamId">The QUIC stream ID the header block was sent on.</param>
    /// <param name="requiredInsertCount">The Required Insert Count from the header block prefix.</param>
    public void TrackSection(int streamId, int requiredInsertCount)
    {
        if (requiredInsertCount > 0)
        {
            _pendingSections[streamId] = requiredInsertCount;
        }
    }

    /// <summary>
    /// RFC 9204 §4.4 — Applies a decoder instruction received on the decoder stream.
    ///
    /// <list type="bullet">
    ///   <item>
    ///     <term>Section Acknowledgment (§4.4.1)</term>
    ///     <description>Updates <see cref="KnownReceivedCount"/> to at least the
    ///     Required Insert Count of the acknowledged section.</description>
    ///   </item>
    ///   <item>
    ///     <term>Insert Count Increment (§4.4.3)</term>
    ///     <description>Directly increments <see cref="KnownReceivedCount"/>.</description>
    ///   </item>
    ///   <item>
    ///     <term>Stream Cancellation (§4.4.2)</term>
    ///     <description>Removes the pending section for the given stream.</description>
    ///   </item>
    /// </list>
    /// </summary>
    public void ApplyDecoderInstruction(DecoderInstruction instruction)
    {
        ArgumentNullException.ThrowIfNull(instruction);

        switch (instruction.Type)
        {
            case DecoderInstructionType.SectionAcknowledgment:
                if (_pendingSections.Remove(instruction.IntValue, out var ric))
                {
                    _knownReceivedCount = Math.Max(_knownReceivedCount, ric);
                }
                break;

            case DecoderInstructionType.InsertCountIncrement:
                if (instruction.IntValue <= 0)
                {
                    throw new QpackException(
                        "RFC 9204 §4.4.3 violation: Insert Count Increment must be positive.");
                }
                _knownReceivedCount += instruction.IntValue;
                break;

            case DecoderInstructionType.StreamCancellation:
                _pendingSections.Remove(instruction.IntValue);
                break;

            default:
                throw new QpackException($"Unknown decoder instruction type: {instruction.Type}");
        }
    }

    /// <summary>
    /// Encodes a list of header fields into a QPACK header block.
    /// After calling this method, check <see cref="EncoderInstructions"/> for
    /// any encoder instructions that must be sent on the encoder stream.
    /// </summary>
    /// <param name="headers">Header fields to encode as (name, value) pairs.</param>
    /// <param name="output">Destination buffer writer for the header block.</param>
    public void Encode(IReadOnlyList<(string Name, string Value)> headers, IBufferWriter<byte> output)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(output);

        _instructionBuffer.Clear();

        // Phase 1: Determine encoding for each header and collect dynamic table inserts.
        // We need to know the Required Insert Count before writing the prefix.
        var encodingPlan = PlanEncodings(headers);

        // Phase 2: Write the header block prefix (Required Insert Count + Base).
        WritePrefix(encodingPlan.RequiredInsertCount, encodingPlan.Base, output);

        // Phase 3: Write each header field representation.
        for (var i = 0; i < headers.Count; i++)
        {
            var (name, value) = headers[i];
            var plan = encodingPlan.Entries[i];

            WriteHeaderField(name, value, plan, encodingPlan.Base, output);
        }
    }

    /// <summary>
    /// Convenience overload that returns the encoded header block as bytes.
    /// </summary>
    public ReadOnlyMemory<byte> Encode(IReadOnlyList<(string Name, string Value)> headers)
    {
        var output = new ArrayBufferWriter<byte>(256);
        Encode(headers, output);
        return output.WrittenMemory;
    }

    // ── Encoding plan ────────────────────────────────────────────────────

    private EncodingPlan PlanEncodings(IReadOnlyList<(string Name, string Value)> headers)
    {
        var entries = new HeaderEncodingEntry[headers.Count];
        var maxAbsoluteIndexReferenced = -1;

        for (var i = 0; i < headers.Count; i++)
        {
            var (name, value) = headers[i];

            if (string.IsNullOrEmpty(name))
            {
                throw new QpackException("RFC 9204 violation: empty header name is not allowed.");
            }

            var isSensitive = SensitiveHeaders.Contains(name);
            var entry = new HeaderEncodingEntry();

            // 1. Try exact match in static table
            var staticExact = QpackStaticTable.FindExact(name, value);
            if (staticExact >= 0 && !isSensitive)
            {
                entry.Type = HeaderEncodingType.StaticIndexed;
                entry.Index = staticExact;
                entries[i] = entry;
                continue;
            }

            // 2. Try exact match in dynamic table
            if (_enableDynamicTable && !isSensitive)
            {
                var dynExact = FindDynamicExact(name, value);
                if (dynExact >= 0)
                {
                    entry.Type = HeaderEncodingType.DynamicIndexed;
                    entry.Index = dynExact;
                    entries[i] = entry;
                    if (dynExact > maxAbsoluteIndexReferenced)
                    {
                        maxAbsoluteIndexReferenced = dynExact;
                    }
                    continue;
                }
            }

            // 3. Try name-only match (static preferred over dynamic)
            var staticName = QpackStaticTable.FindName(name);
            var dynamicName = _enableDynamicTable ? FindDynamicName(name) : -1;

            // 4. Insert into dynamic table if not sensitive and table is enabled
            if (_enableDynamicTable && !isSensitive)
            {
                var insertedIdx = _table.Insert(name, value);
                if (insertedIdx >= 0)
                {
                    // Emit encoder instruction for the insert
                    if (staticName >= 0)
                    {
                        QpackEncoderInstructionWriter.WriteInsertWithNameReference(
                            staticName, true, Encoding.UTF8.GetBytes(value).AsSpan(), _instructionBuffer);
                    }
                    else if (dynamicName >= 0)
                    {
                        // Dynamic table name reference uses relative index in instructions
                        var relIdx = _table.InsertCount - 1 - dynamicName;
                        QpackEncoderInstructionWriter.WriteInsertWithNameReference(
                            relIdx, false, Encoding.UTF8.GetBytes(value).AsSpan(), _instructionBuffer);
                    }
                    else
                    {
                        QpackEncoderInstructionWriter.WriteInsertWithLiteralName(name, value, _instructionBuffer);
                    }

                    // Reference the newly inserted entry
                    entry.Type = HeaderEncodingType.DynamicIndexed;
                    entry.Index = insertedIdx;
                    if (insertedIdx > maxAbsoluteIndexReferenced)
                    {
                        maxAbsoluteIndexReferenced = insertedIdx;
                    }
                    entries[i] = entry;
                    continue;
                }
            }

            // 5. Fall back to literal encoding
            if (isSensitive)
            {
                if (staticName >= 0)
                {
                    entry.Type = HeaderEncodingType.LiteralWithStaticNameNeverIndex;
                    entry.Index = staticName;
                }
                else
                {
                    entry.Type = HeaderEncodingType.LiteralNeverIndex;
                    entry.Index = -1;
                }
            }
            else
            {
                if (staticName >= 0)
                {
                    entry.Type = HeaderEncodingType.LiteralWithStaticName;
                    entry.Index = staticName;
                }
                else if (dynamicName >= 0)
                {
                    entry.Type = HeaderEncodingType.LiteralWithDynamicName;
                    entry.Index = dynamicName;
                    if (dynamicName > maxAbsoluteIndexReferenced)
                    {
                        maxAbsoluteIndexReferenced = dynamicName;
                    }
                }
                else
                {
                    entry.Type = HeaderEncodingType.Literal;
                    entry.Index = -1;
                }
            }

            entries[i] = entry;
        }

        // Required Insert Count = highest absolute index referenced + 1 (or 0 if no dynamic refs)
        var requiredInsertCount = maxAbsoluteIndexReferenced >= 0 ? maxAbsoluteIndexReferenced + 1 : 0;

        // Base = Required Insert Count (simplest: delta base = 0, sign = 0)
        // All dynamic references are pre-base.
        var encodingBase = requiredInsertCount;

        return new EncodingPlan(entries, requiredInsertCount, encodingBase);
    }

    // ── Wire format: prefix ──────────────────────────────────────────────

    /// <summary>
    /// RFC 9204 §4.5.1 — Writes the header block prefix (Required Insert Count + Base).
    ///
    ///   0   1   2   3   4   5   6   7
    /// +---+---+---+---+---+---+---+---+
    /// |   Required Insert Count (8+)  |
    /// +---+---------------------------+
    /// | S |      Delta Base (7+)      |
    /// +---+---------------------------+
    /// </summary>
    private void WritePrefix(int requiredInsertCount, int encodingBase, IBufferWriter<byte> output)
    {
        // Encode Required Insert Count (§4.5.1.1)
        var encodedRic = EncodeRequiredInsertCount(requiredInsertCount);
        QpackIntegerCodec.Encode(encodedRic, 8, 0x00, output);

        // Encode Sign bit + Delta Base (§4.5.1.2)
        if (requiredInsertCount == 0)
        {
            // No dynamic refs: S=0, delta base=0
            QpackIntegerCodec.Encode(0, 7, 0x00, output);
        }
        else if (encodingBase >= requiredInsertCount)
        {
            // S=0, delta base = base - requiredInsertCount
            var deltaBase = encodingBase - requiredInsertCount;
            QpackIntegerCodec.Encode(deltaBase, 7, 0x00, output);
        }
        else
        {
            // S=1, delta base = requiredInsertCount - base - 1
            var deltaBase = requiredInsertCount - encodingBase - 1;
            QpackIntegerCodec.Encode(deltaBase, 7, 0x80, output);
        }
    }

    /// <summary>
    /// RFC 9204 §4.5.1.1 — Encodes the Required Insert Count.
    ///
    /// If RequiredInsertCount is 0, the encoded value is 0.
    /// Otherwise: EncodedInsertCount = (RequiredInsertCount mod (2 * MaxEntries)) + 1
    /// where MaxEntries = floor(MaxTableCapacity / 32).
    /// </summary>
    private int EncodeRequiredInsertCount(int requiredInsertCount)
    {
        if (requiredInsertCount == 0)
        {
            return 0;
        }

        var maxEntries = _maxTableCapacity / QpackDynamicTable.EntryOverhead;

        if (maxEntries == 0)
        {
            throw new QpackException(
                "RFC 9204 §4.5.1.1 violation: Required Insert Count > 0 but MaxEntries is 0 (table capacity too small).");
        }

        return (requiredInsertCount % (2 * maxEntries)) + 1;
    }

    // ── Wire format: header field representations ────────────────────────

    private void WriteHeaderField(string name, string value, HeaderEncodingEntry plan, int encodingBase,
        IBufferWriter<byte> output)
    {
        switch (plan.Type)
        {
            case HeaderEncodingType.StaticIndexed:
                // §4.5.2 — Indexed Header Field (static): 1T=1xxxxxx, 6-bit prefix
                QpackIntegerCodec.Encode(plan.Index, 6, 0xC0, output);
                break;

            case HeaderEncodingType.DynamicIndexed:
                WriteDynamicIndexed(plan.Index, encodingBase, output);
                break;

            case HeaderEncodingType.LiteralWithStaticName:
                // §4.5.4 — Literal with Name Reference (static): 01N=0T=1xxxx, 4-bit prefix
                QpackIntegerCodec.Encode(plan.Index, 4, 0x50, output);
                WriteStringValue(value, output);
                break;

            case HeaderEncodingType.LiteralWithDynamicName:
                WriteLiteralWithDynamicName(name, value, plan.Index, encodingBase, false, output);
                break;

            case HeaderEncodingType.LiteralWithStaticNameNeverIndex:
                // §4.5.4 — Literal with Name Reference (static, never-indexed): 01N=1T=1xxxx, 4-bit prefix
                QpackIntegerCodec.Encode(plan.Index, 4, 0x70, output);
                WriteStringValue(value, output);
                break;

            case HeaderEncodingType.LiteralNeverIndex:
                // §4.5.6 — Literal without Name Reference (never-indexed): 001N=1Hxxx, 3-bit prefix
                WriteLiteralNoNameRef(name, value, true, output);
                break;

            case HeaderEncodingType.Literal:
                // §4.5.6 — Literal without Name Reference: 001N=0Hxxx, 3-bit prefix
                WriteLiteralNoNameRef(name, value, false, output);
                break;

            default:
                throw new QpackException($"Unknown encoding type: {plan.Type}");
        }
    }

    /// <summary>
    /// §4.5.2 / §4.5.3 — Writes an indexed dynamic table reference.
    /// Uses pre-base (§4.5.2) or post-base (§4.5.3) format depending on the index.
    /// </summary>
    private static void WriteDynamicIndexed(int absoluteIndex, int encodingBase, IBufferWriter<byte> output)
    {
        if (absoluteIndex < encodingBase)
        {
            // §4.5.2 — Indexed Header Field (dynamic): 1T=0xxxxxx, 6-bit prefix
            // Relative index = Base - AbsoluteIndex - 1
            var relativeIndex = encodingBase - absoluteIndex - 1;
            QpackIntegerCodec.Encode(relativeIndex, 6, 0x80, output);
        }
        else
        {
            // §4.5.3 — Indexed Header Field with Post-Base Index: 0001xxxx, 4-bit prefix
            var postBaseIndex = absoluteIndex - encodingBase;
            QpackIntegerCodec.Encode(postBaseIndex, 4, 0x10, output);
        }
    }

    /// <summary>
    /// §4.5.4 / §4.5.5 — Writes a literal with dynamic table name reference.
    /// </summary>
    private static void WriteLiteralWithDynamicName(string name, string value, int absoluteIndex, int encodingBase,
        bool neverIndex, IBufferWriter<byte> output)
    {
        if (absoluteIndex < encodingBase)
        {
            // §4.5.4 — Literal with Name Reference (dynamic): 01NTxxxx, 4-bit prefix
            // N bit at 0x20, T=0 for dynamic
            var flags = (byte)(0x40 | (neverIndex ? 0x20 : 0x00));
            var relativeIndex = encodingBase - absoluteIndex - 1;
            QpackIntegerCodec.Encode(relativeIndex, 4, flags, output);
        }
        else
        {
            // §4.5.5 — Literal with Post-Base Name Reference: 0000Nxxx, 3-bit prefix
            var flags = (byte)(neverIndex ? 0x08 : 0x00);
            var postBaseIndex = absoluteIndex - encodingBase;
            QpackIntegerCodec.Encode(postBaseIndex, 3, flags, output);
        }

        WriteStringValue(value, output);
    }

    /// <summary>
    /// §4.5.6 — Writes a literal header field without name reference.
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
    private static void WriteLiteralNoNameRef(string name, string value, bool neverIndex, IBufferWriter<byte> output)
    {
        // Name: 001NHxxx → prefix flags = 0x20 | (N ? 0x10 : 0x00), H bit managed by QpackStringCodec
        var nameFlags = (byte)(0x20 | (neverIndex ? 0x10 : 0x00));
        QpackStringCodec.Encode(Encoding.UTF8.GetBytes(name).AsSpan(), 3, nameFlags, output);

        // Value: Hxxxxxxx, 7-bit prefix
        WriteStringValue(value, output);
    }

    /// <summary>Writes a string value with 7-bit prefix and auto Huffman selection.</summary>
    private static void WriteStringValue(string value, IBufferWriter<byte> output)
    {
        QpackStringCodec.Encode(Encoding.UTF8.GetBytes(value).AsSpan(), 7, 0x00, output);
    }

    // ── Dynamic table lookup ─────────────────────────────────────────────

    /// <summary>
    /// Finds an exact (name, value) match in the dynamic table.
    /// Returns the absolute index, or -1 if not found.
    /// </summary>
    private int FindDynamicExact(string name, string value)
    {
        // Search from newest to oldest for best locality
        for (var i = _table.InsertCount - 1; i >= _table.DroppedCount; i--)
        {
            var entry = _table.GetEntry(i);
            if (entry is null)
            {
                continue;
            }

            if (string.Equals(entry.Value.Name, name, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(entry.Value.Value, value, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Finds a name-only match in the dynamic table.
    /// Returns the absolute index, or -1 if not found.
    /// </summary>
    private int FindDynamicName(string name)
    {
        for (var i = _table.InsertCount - 1; i >= _table.DroppedCount; i--)
        {
            var entry = _table.GetEntry(i);
            if (entry is null)
            {
                continue;
            }

            if (string.Equals(entry.Value.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    // ── Internal types ───────────────────────────────────────────────────

    private enum HeaderEncodingType
    {
        StaticIndexed,
        DynamicIndexed,
        LiteralWithStaticName,
        LiteralWithDynamicName,
        LiteralWithStaticNameNeverIndex,
        LiteralNeverIndex,
        Literal,
    }

    private struct HeaderEncodingEntry
    {
        public HeaderEncodingType Type;
        public int Index;
    }

    private readonly record struct EncodingPlan(
        HeaderEncodingEntry[] Entries,
        int RequiredInsertCount,
        int Base);
}
