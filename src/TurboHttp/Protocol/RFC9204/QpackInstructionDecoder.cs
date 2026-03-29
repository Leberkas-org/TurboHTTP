using System.Text;

namespace TurboHttp.Protocol.RFC9204;

/// <summary>
/// Decode status for QPACK instruction parsing.
/// </summary>
public enum QpackDecodeStatus
{
    /// <summary>An instruction was successfully decoded.</summary>
    Success,

    /// <summary>Not enough data to decode a complete instruction.</summary>
    NeedMoreData
}

/// <summary>
/// Discriminator for encoder instruction types (RFC 9204 §4.3).
/// </summary>
public enum EncoderInstructionType
{
    SetDynamicTableCapacity,
    InsertWithNameReference,
    InsertWithLiteralName,
    Duplicate
}

/// <summary>
/// Discriminator for decoder instruction types (RFC 9204 §4.4).
/// </summary>
public enum DecoderInstructionType
{
    SectionAcknowledgment,
    StreamCancellation,
    InsertCountIncrement
}

/// <summary>
/// Parsed encoder instruction (RFC 9204 §4.3).
/// </summary>
public sealed class EncoderInstruction
{
    public EncoderInstructionType Type { get; init; }

    /// <summary>Set Dynamic Table Capacity value, or Duplicate index.</summary>
    public int IntValue { get; init; }

    /// <summary>Insert With Name Reference: name index.</summary>
    public int NameIndex { get; init; }

    /// <summary>Insert With Name Reference: true if static table.</summary>
    public bool IsStatic { get; init; }

    /// <summary>Insert With Name Reference / Literal Name: header name bytes (UTF-8).</summary>
    public byte[] Name { get; init; } = [];

    /// <summary>Insert instructions: header value bytes (UTF-8).</summary>
    public byte[] Value { get; init; } = [];

    /// <summary>Helper: Name as string.</summary>
    public string NameString => Encoding.UTF8.GetString(Name);

    /// <summary>Helper: Value as string.</summary>
    public string ValueString => Encoding.UTF8.GetString(Value);
}

/// <summary>
/// Parsed decoder instruction (RFC 9204 §4.4).
/// </summary>
public sealed class DecoderInstruction
{
    public DecoderInstructionType Type { get; init; }

    /// <summary>Stream ID (for Section Acknowledgment and Stream Cancellation) or increment value.</summary>
    public int IntValue { get; init; }
}

/// <summary>
/// RFC 9204 §4.3, §4.4 — Stateful decoder for QPACK instruction streams.
///
/// Parses encoder instructions (encoder→decoder stream):
///   - Set Dynamic Table Capacity (§4.3.1): 001xxxxx
///   - Insert With Name Reference (§4.3.2): 1Txxxxxx + value
///   - Insert With Literal Name (§4.3.3):   01Hxxxxx + name + value
///   - Duplicate (§4.3.4):                  000xxxxx
///
/// Parses decoder instructions (decoder→encoder stream):
///   - Section Acknowledgment (§4.4.1):     1xxxxxxx
///   - Stream Cancellation (§4.4.2):        01xxxxxx
///   - Insert Count Increment (§4.4.3):     00xxxxxx
///
/// Maintains a remainder buffer for partial instructions split across reads.
/// </summary>
public sealed class QpackInstructionDecoder
{
    private byte[] _remainder = [];

    /// <summary>True if there is unconsumed data from a previous call.</summary>
    public bool HasRemainder => _remainder.Length > 0;

    /// <summary>Resets the decoder state, clearing any buffered remainder.</summary>
    public void Reset()
    {
        _remainder = [];
    }

    /// <summary>
    /// Attempts to decode one encoder instruction (RFC 9204 §4.3).
    /// </summary>
    /// <param name="data">Input data (appended to any existing remainder).</param>
    /// <param name="instruction">The decoded instruction, or null if more data is needed.</param>
    /// <returns><see cref="QpackDecodeStatus.Success"/> if an instruction was decoded.</returns>
    public QpackDecodeStatus TryDecodeEncoderInstruction(ReadOnlySpan<byte> data, out EncoderInstruction? instruction)
    {
        instruction = null;
        var buffer = Combine(_remainder, data);

        if (buffer.Length == 0)
        {
            _remainder = [];
            return QpackDecodeStatus.NeedMoreData;
        }

        var pos = 0;
        var span = buffer.AsSpan();

        try
        {
            var firstByte = span[0];

            if ((firstByte & 0x80) != 0)
            {
                // §4.3.2 — Insert With Name Reference: 1Txxxxxx
                var isStatic = (firstByte & 0x40) != 0;
                var nameIndex = QpackIntegerCodec.Decode(span, ref pos, 6);
                var value = QpackStringCodec.Decode(span, ref pos, 7);

                instruction = new EncoderInstruction
                {
                    Type = EncoderInstructionType.InsertWithNameReference,
                    NameIndex = nameIndex,
                    IsStatic = isStatic,
                    Value = value
                };
            }
            else if ((firstByte & 0x40) != 0)
            {
                // §4.3.3 — Insert With Literal Name: 01Hxxxxx
                var name = QpackStringCodec.Decode(span, ref pos, 5);
                var value = QpackStringCodec.Decode(span, ref pos, 7);

                instruction = new EncoderInstruction
                {
                    Type = EncoderInstructionType.InsertWithLiteralName,
                    Name = name,
                    Value = value
                };
            }
            else if ((firstByte & 0x20) != 0)
            {
                // §4.3.1 — Set Dynamic Table Capacity: 001xxxxx
                var capacity = QpackIntegerCodec.Decode(span, ref pos, 5);

                instruction = new EncoderInstruction
                {
                    Type = EncoderInstructionType.SetDynamicTableCapacity,
                    IntValue = capacity
                };
            }
            else
            {
                // §4.3.4 — Duplicate: 000xxxxx
                var index = QpackIntegerCodec.Decode(span, ref pos, 5);

                instruction = new EncoderInstruction
                {
                    Type = EncoderInstructionType.Duplicate,
                    IntValue = index
                };
            }

            _remainder = pos < buffer.Length ? buffer[pos..] : [];
            return QpackDecodeStatus.Success;
        }
        catch (QpackException)
        {
            // Integer or string codec threw because data was truncated — need more data
            _remainder = buffer;
            return QpackDecodeStatus.NeedMoreData;
        }
    }

    /// <summary>
    /// Attempts to decode one decoder instruction (RFC 9204 §4.4).
    /// </summary>
    /// <param name="data">Input data (appended to any existing remainder).</param>
    /// <param name="instruction">The decoded instruction, or null if more data is needed.</param>
    /// <returns><see cref="QpackDecodeStatus.Success"/> if an instruction was decoded.</returns>
    public QpackDecodeStatus TryDecodeDecoderInstruction(ReadOnlySpan<byte> data, out DecoderInstruction? instruction)
    {
        instruction = null;
        var buffer = Combine(_remainder, data);

        if (buffer.Length == 0)
        {
            _remainder = [];
            return QpackDecodeStatus.NeedMoreData;
        }

        var pos = 0;
        var span = buffer.AsSpan();

        try
        {
            var firstByte = span[0];

            if ((firstByte & 0x80) != 0)
            {
                // §4.4.1 — Section Acknowledgment: 1xxxxxxx
                var streamId = QpackIntegerCodec.Decode(span, ref pos, 7);

                instruction = new DecoderInstruction
                {
                    Type = DecoderInstructionType.SectionAcknowledgment,
                    IntValue = streamId
                };
            }
            else if ((firstByte & 0x40) != 0)
            {
                // §4.4.2 — Stream Cancellation: 01xxxxxx
                var streamId = QpackIntegerCodec.Decode(span, ref pos, 6);

                instruction = new DecoderInstruction
                {
                    Type = DecoderInstructionType.StreamCancellation,
                    IntValue = streamId
                };
            }
            else
            {
                // §4.4.3 — Insert Count Increment: 00xxxxxx
                var increment = QpackIntegerCodec.Decode(span, ref pos, 6);

                instruction = new DecoderInstruction
                {
                    Type = DecoderInstructionType.InsertCountIncrement,
                    IntValue = increment
                };
            }

            _remainder = pos < buffer.Length ? buffer[pos..] : [];
            return QpackDecodeStatus.Success;
        }
        catch (QpackException)
        {
            _remainder = buffer;
            return QpackDecodeStatus.NeedMoreData;
        }
    }

    /// <summary>
    /// Decodes all encoder instructions from the given data.
    /// Partial trailing data is buffered for the next call.
    /// </summary>
    public EncoderInstruction[] DecodeAllEncoderInstructions(ReadOnlySpan<byte> data)
    {
        var results = new System.Collections.Generic.List<EncoderInstruction>();
        var first = true;

        while (true)
        {
            var input = first ? data : ReadOnlySpan<byte>.Empty;
            first = false;

            var status = TryDecodeEncoderInstruction(input, out var instruction);

            if (status == QpackDecodeStatus.NeedMoreData)
            {
                break;
            }

            results.Add(instruction!);
        }

        return results.ToArray();
    }

    /// <summary>
    /// Decodes all decoder instructions from the given data.
    /// Partial trailing data is buffered for the next call.
    /// </summary>
    public DecoderInstruction[] DecodeAllDecoderInstructions(ReadOnlySpan<byte> data)
    {
        var results = new System.Collections.Generic.List<DecoderInstruction>();
        var first = true;

        while (true)
        {
            var input = first ? data : ReadOnlySpan<byte>.Empty;
            first = false;

            var status = TryDecodeDecoderInstruction(input, out var instruction);

            if (status == QpackDecodeStatus.NeedMoreData)
            {
                break;
            }

            results.Add(instruction!);
        }

        return results.ToArray();
    }

    private static byte[] Combine(byte[] remainder, ReadOnlySpan<byte> data)
    {
        if (remainder.Length == 0 && data.Length == 0)
        {
            return [];
        }

        if (remainder.Length == 0)
        {
            return data.ToArray();
        }

        if (data.Length == 0)
        {
            return remainder;
        }

        var combined = new byte[remainder.Length + data.Length];
        remainder.CopyTo(combined, 0);
        data.CopyTo(combined.AsSpan(remainder.Length));
        return combined;
    }
}
