using System.Buffers;
using System.Text;

namespace TurboHTTP.Protocol.Http3.Qpack;

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
    /// <param name="output">Destination span, advanced past written bytes.</param>
    /// <returns>Number of bytes written.</returns>
    public static int WriteSetDynamicTableCapacity(int capacity, ref Span<byte> output)
    {
        if (capacity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be non-negative.");
        }

        // Prefix: 001xxxxx → prefixFlags = 0x20, prefixBits = 5
        return QpackIntegerCodec.Encode(capacity, 5, 0x20, ref output);
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
    /// <param name="output">Destination span, advanced past written bytes.</param>
    /// <returns>Number of bytes written.</returns>
    public static int WriteInsertWithNameReference(int nameIndex, bool isStatic, ReadOnlySpan<byte> value, ref Span<byte> output)
    {
        if (nameIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(nameIndex), "Name index must be non-negative.");
        }

        var total = 0;

        // First byte: 1Txxxxxx → high bit = 0x80, T bit = 0x40
        // prefixBits = 6
        var prefixFlags = (byte)(0x80 | (isStatic ? 0x40 : 0x00));
        total += QpackIntegerCodec.Encode(nameIndex, 6, prefixFlags, ref output);

        // Value string: H bit + length (7-bit prefix) + data
        total += QpackStringCodec.Encode(value, 7, 0x00, ref output);

        return total;
    }

    /// <summary>
    /// RFC 9204 §4.3.2 — Insert With Name Reference (string overload).
    /// Encodes the string value as UTF-8 directly into the output span.
    /// </summary>
    /// <returns>Number of bytes written.</returns>
    public static int WriteInsertWithNameReference(int nameIndex, bool isStatic, string value, ref Span<byte> output)
    {
        var maxByteCount = Encoding.UTF8.GetMaxByteCount(value.Length);
        using var owner = MemoryPool<byte>.Shared.Rent(maxByteCount);
        var utf8Span = owner.Memory.Span[..maxByteCount];
        var written = Encoding.UTF8.GetBytes(value.AsSpan(), utf8Span);
        return WriteInsertWithNameReference(nameIndex, isStatic, utf8Span[..written], ref output);
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
    /// <param name="output">Destination span, advanced past written bytes.</param>
    /// <returns>Number of bytes written.</returns>
    public static int WriteInsertWithLiteralName(ReadOnlySpan<byte> name, ReadOnlySpan<byte> value, ref Span<byte> output)
    {
        var total = 0;

        // Name string: 01Hxxxxx → prefixFlags = 0x40, prefixBits = 5
        // The H bit is at bit 5 (0x20), handled internally by QpackStringCodec
        total += QpackStringCodec.Encode(name, 5, 0x40, ref output);

        // Value string: Hxxxxxxx → prefixFlags = 0x00, prefixBits = 7
        total += QpackStringCodec.Encode(value, 7, 0x00, ref output);

        return total;
    }

    /// <summary>
    /// RFC 9204 §4.3.3 — Insert With Literal Name (string overload).
    /// Encodes both name and value as UTF-8 directly into the output span.
    /// </summary>
    /// <returns>Number of bytes written.</returns>
    public static int WriteInsertWithLiteralName(string name, string value, ref Span<byte> output)
    {
        var maxNameBytes = Encoding.UTF8.GetMaxByteCount(name.Length);
        var maxValueBytes = Encoding.UTF8.GetMaxByteCount(value.Length);
        using var owner = MemoryPool<byte>.Shared.Rent(maxNameBytes + maxValueBytes);

        var nameSpan = owner.Memory.Span[..maxNameBytes];
        var nameWritten = Encoding.UTF8.GetBytes(name.AsSpan(), nameSpan);

        var valueSpan = owner.Memory.Span.Slice(maxNameBytes, maxValueBytes);
        var valueWritten = Encoding.UTF8.GetBytes(value.AsSpan(), valueSpan);

        return WriteInsertWithLiteralName(nameSpan[..nameWritten], valueSpan[..valueWritten], ref output);
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
    /// <param name="output">Destination span, advanced past written bytes.</param>
    /// <returns>Number of bytes written.</returns>
    public static int WriteDuplicate(int index, ref Span<byte> output)
    {
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index), "Index must be non-negative.");
        }

        // Prefix: 000xxxxx → prefixFlags = 0x00, prefixBits = 5
        return QpackIntegerCodec.Encode(index, 5, 0x00, ref output);
    }
}
