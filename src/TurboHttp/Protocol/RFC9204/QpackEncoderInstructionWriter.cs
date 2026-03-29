using System.Buffers;
using System.Text;

namespace TurboHttp.Protocol.RFC9204;

/// <summary>
/// RFC 9204 §4.3 — Writes encoder instructions to the encoder stream.
///
/// Encoder instructions are sent from encoder to decoder to modify the dynamic table:
///   - Set Dynamic Table Capacity (§4.3.1)
///   - Insert With Name Reference (§4.3.2)
///   - Insert With Literal Name (§4.3.3)
///   - Duplicate (§4.3.4)
/// </summary>
public static class QpackEncoderInstructionWriter
{
    /// <summary>
    /// RFC 9204 §4.3.1 — Set Dynamic Table Capacity.
    ///
    ///   0   1   2   3   4   5   6   7
    /// +---+---+---+---+---+---+---+---+
    /// | 0 | 0 | 1 |   Capacity (5+)   |
    /// +---+---+---+-------------------+
    /// </summary>
    /// <param name="capacity">The new dynamic table capacity in bytes (must be non-negative).</param>
    /// <param name="output">Destination buffer writer.</param>
    public static void WriteSetDynamicTableCapacity(int capacity, IBufferWriter<byte> output)
    {
        if (capacity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be non-negative.");
        }

        // Prefix: 001xxxxx → prefixFlags = 0x20, prefixBits = 5
        QpackIntegerCodec.Encode(capacity, 5, 0x20, output);
    }

    /// <summary>
    /// RFC 9204 §4.3.2 — Insert With Name Reference.
    ///
    ///   0   1   2   3   4   5   6   7
    /// +---+---+---+---+---+---+---+---+
    /// | 1 | T |  Name Index (6+)      |
    /// +---+---+-----------------------+
    /// | H |  Value Length (7+)        |
    /// +---+---------------------------+
    /// | Value String (Length bytes)    |
    /// +-------------------------------+
    ///
    /// T=1 references the static table, T=0 references the dynamic table.
    /// </summary>
    /// <param name="nameIndex">Index into static or dynamic table.</param>
    /// <param name="isStatic">True to reference static table (T=1), false for dynamic (T=0).</param>
    /// <param name="value">The header value as UTF-8 bytes.</param>
    /// <param name="output">Destination buffer writer.</param>
    public static void WriteInsertWithNameReference(int nameIndex, bool isStatic, ReadOnlySpan<byte> value, IBufferWriter<byte> output)
    {
        if (nameIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(nameIndex), "Name index must be non-negative.");
        }

        // First byte: 1Txxxxxx → high bit = 0x80, T bit = 0x40
        // prefixBits = 6
        var prefixFlags = (byte)(0x80 | (isStatic ? 0x40 : 0x00));
        QpackIntegerCodec.Encode(nameIndex, 6, prefixFlags, output);

        // Value string: H bit + length (7-bit prefix) + data
        QpackStringCodec.Encode(value, 7, 0x00, output);
    }

    /// <summary>
    /// RFC 9204 §4.3.2 — Insert With Name Reference (string overload).
    /// </summary>
    public static void WriteInsertWithNameReference(int nameIndex, bool isStatic, string value, IBufferWriter<byte> output)
    {
        WriteInsertWithNameReference(nameIndex, isStatic, Encoding.UTF8.GetBytes(value).AsSpan(), output);
    }

    /// <summary>
    /// RFC 9204 §4.3.3 — Insert With Literal Name.
    ///
    ///   0   1   2   3   4   5   6   7
    /// +---+---+---+---+---+---+---+---+
    /// | 0 | 1 | H |  Name Length (5+) |
    /// +---+---+---+-------------------+
    /// | Name String (Length bytes)     |
    /// +---+---------------------------+
    /// | H |  Value Length (7+)        |
    /// +---+---------------------------+
    /// | Value String (Length bytes)    |
    /// +-------------------------------+
    /// </summary>
    /// <param name="name">The header name as UTF-8 bytes.</param>
    /// <param name="value">The header value as UTF-8 bytes.</param>
    /// <param name="output">Destination buffer writer.</param>
    public static void WriteInsertWithLiteralName(ReadOnlySpan<byte> name, ReadOnlySpan<byte> value, IBufferWriter<byte> output)
    {
        // Name string: 01Hxxxxx → prefixFlags = 0x40, prefixBits = 5
        // The H bit is at bit 5 (0x20), handled internally by QpackStringCodec
        QpackStringCodec.Encode(name, 5, 0x40, output);

        // Value string: Hxxxxxxx → prefixFlags = 0x00, prefixBits = 7
        QpackStringCodec.Encode(value, 7, 0x00, output);
    }

    /// <summary>
    /// RFC 9204 §4.3.3 — Insert With Literal Name (string overload).
    /// </summary>
    public static void WriteInsertWithLiteralName(string name, string value, IBufferWriter<byte> output)
    {
        WriteInsertWithLiteralName(
            Encoding.UTF8.GetBytes(name).AsSpan(),
            Encoding.UTF8.GetBytes(value).AsSpan(),
            output);
    }

    /// <summary>
    /// RFC 9204 §4.3.4 — Duplicate.
    ///
    ///   0   1   2   3   4   5   6   7
    /// +---+---+---+---+---+---+---+---+
    /// | 0 | 0 | 0 |   Index (5+)      |
    /// +---+---+---+-------------------+
    ///
    /// The index is the relative index in the dynamic table.
    /// </summary>
    /// <param name="index">Relative index in the dynamic table.</param>
    /// <param name="output">Destination buffer writer.</param>
    public static void WriteDuplicate(int index, IBufferWriter<byte> output)
    {
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index), "Index must be non-negative.");
        }

        // Prefix: 000xxxxx → prefixFlags = 0x00, prefixBits = 5
        QpackIntegerCodec.Encode(index, 5, 0x00, output);
    }
}
