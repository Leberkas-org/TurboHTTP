using System.Buffers;
using System.Text;

namespace TurboHTTP.Protocol.Syntax.Http3.Qpack;

internal sealed class QpackEncoder
{
    private static readonly HashSet<string> SensitiveHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        WellKnownHeaders.Authorization,
        WellKnownHeaders.ProxyAuthorization,
        WellKnownHeaders.Cookie,
        WellKnownHeaders.SetCookie
    };

    private int _maxTableCapacity;
    private bool _enableDynamicTable;
    private IMemoryOwner<byte>? _instructionOwner;
    private int _instructionBytesWritten;
    private readonly Dictionary<int, int> _pendingSections = new();

    public QpackEncoder(int maxTableCapacity = 4096)
    {
        if (maxTableCapacity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxTableCapacity), "Capacity must be non-negative.");
        }

        _maxTableCapacity = maxTableCapacity;
        DynamicTable = new QpackDynamicTable(maxTableCapacity);
        _enableDynamicTable = maxTableCapacity > 0;
    }

    public QpackDynamicTable DynamicTable { get; }

    public void SetMaxCapacity(int capacity)
    {
        if (capacity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be non-negative.");
        }

        _maxTableCapacity = capacity;
        _enableDynamicTable = capacity > 0;
        DynamicTable.SetCapacity(capacity);

        EnsureInstructionBuffer(16);
        _instructionBytesWritten = 0;

        var w = SpanWriter.Create(_instructionOwner!.Memory.Span);
        _instructionBytesWritten = QpackEncoderInstructionWriter.WriteSetDynamicTableCapacity(capacity, ref w);
    }

    public ReadOnlyMemory<byte> EncoderInstructions =>
        _instructionOwner?.Memory[.._instructionBytesWritten] ?? ReadOnlyMemory<byte>.Empty;

    public int KnownReceivedCount { get; private set; }

    public void TrackSection(int streamId, int requiredInsertCount)
    {
        if (requiredInsertCount > 0)
        {
            _pendingSections[streamId] = requiredInsertCount;
        }
    }

    public void ApplyDecoderInstruction(DecoderInstruction instruction)
    {
        ArgumentNullException.ThrowIfNull(instruction);

        switch (instruction.Type)
        {
            case DecoderInstructionType.SectionAcknowledgment:
                if (_pendingSections.Remove(instruction.IntValue, out var ric))
                {
                    KnownReceivedCount = Math.Max(KnownReceivedCount, ric);
                }

                break;

            case DecoderInstructionType.InsertCountIncrement:
                if (instruction.IntValue <= 0)
                {
                    throw new QpackException("RFC 9204 §4.4.3 violation: Insert Count Increment must be positive.");
                }

                KnownReceivedCount += instruction.IntValue;
                break;

            case DecoderInstructionType.StreamCancellation:
                _pendingSections.Remove(instruction.IntValue);
                break;

            default:
                throw new QpackException($"Unknown decoder instruction type: {instruction.Type}");
        }
    }

    public int Encode(IReadOnlyList<(string Name, string Value)> headers, ref SpanWriter writer)
    {
        ArgumentNullException.ThrowIfNull(headers);

        EnsureInstructionBuffer(1024);
        _instructionBytesWritten = 0;

        var startWritten = writer.BytesWritten;

        var encodingPlan = PlanEncodings(headers);

        WritePrefix(encodingPlan.RequiredInsertCount, encodingPlan.Base, ref writer);

        for (var i = 0; i < headers.Count; i++)
        {
            var (name, value) = headers[i];
            var plan = encodingPlan.Entries[i];

            WriteHeaderField(name, value, plan, encodingPlan.Base, ref writer);
        }

        return writer.BytesWritten - startWritten;
    }

    public ReadOnlyMemory<byte> Encode(IReadOnlyList<(string Name, string Value)> headers)
    {
        using var owner = MemoryPool<byte>.Shared.Rent(8192);
        var writer = SpanWriter.Create(owner.Memory.Span);
        var n = Encode(headers, ref writer);
        return owner.Memory[..n].ToArray();
    }

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

            entries[i] = ClassifyHeader(name, value, ref maxAbsoluteIndexReferenced);
        }

        var requiredInsertCount = maxAbsoluteIndexReferenced >= 0 ? maxAbsoluteIndexReferenced + 1 : 0;
        return new EncodingPlan(entries, requiredInsertCount, requiredInsertCount);
    }

    private HeaderEncodingEntry ClassifyHeader(string name, string value, ref int maxAbsoluteIndexReferenced)
    {
        var isSensitive = SensitiveHeaders.Contains(name);

        if (TryExactMatch(name, value, isSensitive, ref maxAbsoluteIndexReferenced, out var entry))
        {
            return entry;
        }

        var staticName = QpackStaticTable.FindName(name);
        var dynamicName = _enableDynamicTable ? FindDynamicName(name) : -1;

        if (TryDynamicInsert(name, value, isSensitive, staticName, dynamicName, ref maxAbsoluteIndexReferenced,
                out entry))
        {
            return entry;
        }

        return BuildLiteralEntry(isSensitive, staticName, dynamicName, ref maxAbsoluteIndexReferenced);
    }

    private bool TryExactMatch(string name, string value, bool isSensitive, ref int maxAbsoluteIndexReferenced,
        out HeaderEncodingEntry entry)
    {
        entry = default;

        if (isSensitive)
        {
            return false;
        }

        var staticExact = QpackStaticTable.FindExact(name, value);
        if (staticExact >= 0)
        {
            entry = new HeaderEncodingEntry { Type = HeaderEncodingType.StaticIndexed, Index = staticExact };
            return true;
        }

        if (!_enableDynamicTable)
        {
            return false;
        }

        var dynExact = FindDynamicExact(name, value);
        if (dynExact >= 0)
        {
            entry = new HeaderEncodingEntry { Type = HeaderEncodingType.DynamicIndexed, Index = dynExact };
            if (dynExact > maxAbsoluteIndexReferenced)
            {
                maxAbsoluteIndexReferenced = dynExact;
            }

            return true;
        }

        return false;
    }

    private bool TryDynamicInsert(string name, string value, bool isSensitive, int staticName, int dynamicName,
        ref int maxAbsoluteIndexReferenced, out HeaderEncodingEntry entry)
    {
        entry = default;

        if (isSensitive || !_enableDynamicTable)
        {
            return false;
        }

        var insertedIdx = DynamicTable.Insert(name, value);
        if (insertedIdx < 0)
        {
            return false;
        }

        EmitInsertInstruction(staticName, dynamicName, name, value);

        entry = new HeaderEncodingEntry { Type = HeaderEncodingType.DynamicIndexed, Index = insertedIdx };
        if (insertedIdx > maxAbsoluteIndexReferenced)
        {
            maxAbsoluteIndexReferenced = insertedIdx;
        }

        return true;
    }

    private void EmitInsertInstruction(int staticName, int dynamicName, string name, string value)
    {
        if (staticName >= 0)
        {
            WriteInstructionToBuffer((ref w) => QpackEncoderInstructionWriter.WriteInsertWithNameReference(
                staticName, true, value, ref w));
        }
        else if (dynamicName >= 0)
        {
            var relIdx = DynamicTable.InsertCount - 1 - dynamicName;
            WriteInstructionToBuffer((ref w) => QpackEncoderInstructionWriter.WriteInsertWithNameReference(
                relIdx, false, value, ref w));
        }
        else
        {
            WriteInstructionToBuffer((ref w) => QpackEncoderInstructionWriter.WriteInsertWithLiteralName(
                name, value, ref w));
        }
    }

    private static HeaderEncodingEntry BuildLiteralEntry(bool isSensitive, int staticName, int dynamicName,
        ref int maxAbsoluteIndexReferenced)
    {
        if (isSensitive)
        {
            return staticName switch
            {
                >= 0 => new HeaderEncodingEntry
                {
                    Type = HeaderEncodingType.LiteralWithStaticNameNeverIndex, Index = staticName
                },
                _ => new HeaderEncodingEntry { Type = HeaderEncodingType.LiteralNeverIndex, Index = -1 }
            };
        }

        if (staticName >= 0)
        {
            return new HeaderEncodingEntry { Type = HeaderEncodingType.LiteralWithStaticName, Index = staticName };
        }

        if (dynamicName >= 0)
        {
            if (dynamicName > maxAbsoluteIndexReferenced)
            {
                maxAbsoluteIndexReferenced = dynamicName;
            }

            return new HeaderEncodingEntry { Type = HeaderEncodingType.LiteralWithDynamicName, Index = dynamicName };
        }

        return new HeaderEncodingEntry { Type = HeaderEncodingType.Literal, Index = -1 };
    }

    private void WritePrefix(int requiredInsertCount, int encodingBase, ref SpanWriter writer)
    {
        var encodedRic = EncodeRequiredInsertCount(requiredInsertCount);
        QpackIntegerCodec.Encode(encodedRic, 8, 0x00, ref writer);

        if (requiredInsertCount == 0)
        {
            QpackIntegerCodec.Encode(0, 7, 0x00, ref writer);
        }
        else if (encodingBase >= requiredInsertCount)
        {
            var deltaBase = encodingBase - requiredInsertCount;
            QpackIntegerCodec.Encode(deltaBase, 7, 0x00, ref writer);
        }
        else
        {
            var deltaBase = requiredInsertCount - encodingBase - 1;
            QpackIntegerCodec.Encode(deltaBase, 7, 0x80, ref writer);
        }
    }

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

        return requiredInsertCount % (2 * maxEntries) + 1;
    }

    private void WriteHeaderField(string name, string value, HeaderEncodingEntry plan, int encodingBase,
        ref SpanWriter writer)
    {
        switch (plan.Type)
        {
            case HeaderEncodingType.StaticIndexed:
                QpackIntegerCodec.Encode(plan.Index, 6, 0xC0, ref writer);
                break;

            case HeaderEncodingType.DynamicIndexed:
                WriteDynamicIndexed(plan.Index, encodingBase, ref writer);
                break;

            case HeaderEncodingType.LiteralWithStaticName:
                QpackIntegerCodec.Encode(plan.Index, 4, 0x50, ref writer);
                WriteStringValue(value, ref writer);
                break;

            case HeaderEncodingType.LiteralWithDynamicName:
                WriteLiteralWithDynamicName(value, plan.Index, encodingBase, false, ref writer);
                break;

            case HeaderEncodingType.LiteralWithStaticNameNeverIndex:
                QpackIntegerCodec.Encode(plan.Index, 4, 0x70, ref writer);
                WriteStringValue(value, ref writer);
                break;

            case HeaderEncodingType.LiteralNeverIndex:
                WriteLiteralNoNameRef(name, value, true, ref writer);
                break;

            case HeaderEncodingType.Literal:
                WriteLiteralNoNameRef(name, value, false, ref writer);
                break;

            default:
                throw new QpackException($"Unknown encoding type: {plan.Type}");
        }
    }

    private static void WriteDynamicIndexed(int absoluteIndex, int encodingBase, ref SpanWriter writer)
    {
        if (absoluteIndex < encodingBase)
        {
            var relativeIndex = encodingBase - absoluteIndex - 1;
            QpackIntegerCodec.Encode(relativeIndex, 6, 0x80, ref writer);
        }
        else
        {
            var postBaseIndex = absoluteIndex - encodingBase;
            QpackIntegerCodec.Encode(postBaseIndex, 4, 0x10, ref writer);
        }
    }

    private static void WriteLiteralWithDynamicName(string value, int absoluteIndex, int encodingBase,
        bool neverIndex, ref SpanWriter writer)
    {
        if (absoluteIndex < encodingBase)
        {
            var flags = (byte)(0x40 | (neverIndex ? 0x20 : 0x00));
            var relativeIndex = encodingBase - absoluteIndex - 1;
            QpackIntegerCodec.Encode(relativeIndex, 4, flags, ref writer);
        }
        else
        {
            var flags = (byte)(neverIndex ? 0x08 : 0x00);
            var postBaseIndex = absoluteIndex - encodingBase;
            QpackIntegerCodec.Encode(postBaseIndex, 3, flags, ref writer);
        }

        WriteStringValue(value, ref writer);
    }

    private static void WriteLiteralNoNameRef(string name, string value, bool neverIndex, ref SpanWriter writer)
    {
        var nameFlags = (byte)(0x20 | (neverIndex ? 0x10 : 0x00));
        WriteStringToOutput(name, 3, nameFlags, ref writer);
        WriteStringValue(value, ref writer);
    }

    private static void WriteStringValue(string value, ref SpanWriter writer)
    {
        WriteStringToOutput(value, 7, 0x00, ref writer);
    }

    private static void WriteStringToOutput(string value, int prefixBits, byte prefixFlags, ref SpanWriter writer)
    {
        var rawLength = Encoding.UTF8.GetByteCount(value);
        if (rawLength == 0)
        {
            QpackStringCodec.Encode(ReadOnlySpan<byte>.Empty, prefixBits, prefixFlags, ref writer);
            return;
        }

        var utf8Start = writer.Remaining.Length - rawLength;
        var utf8Region = writer.Remaining[utf8Start..];
        Encoding.UTF8.GetBytes(value.AsSpan(), utf8Region);
        QpackStringCodec.Encode(utf8Region[..rawLength], prefixBits, prefixFlags, ref writer);
    }

    private int FindDynamicExact(string name, string value)
    {
        for (var i = DynamicTable.Count - 1; i >= 0; i--)
        {
            var (absoluteIndex, entry, _) = DynamicTable[i];
            if (string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(entry.Value, value, StringComparison.Ordinal))
            {
                return absoluteIndex;
            }
        }

        return -1;
    }

    private int FindDynamicName(string name)
    {
        for (var i = DynamicTable.Count - 1; i >= 0; i--)
        {
            var (absoluteIndex, entry, _) = DynamicTable[i];
            if (string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return absoluteIndex;
            }
        }

        return -1;
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

    private delegate int InstructionWriterFunc(ref SpanWriter w);

    private void WriteInstructionToBuffer(InstructionWriterFunc func)
    {
        var w = SpanWriter.Create(_instructionOwner!.Memory.Span[_instructionBytesWritten..]);
        _instructionBytesWritten += func(ref w);
    }

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