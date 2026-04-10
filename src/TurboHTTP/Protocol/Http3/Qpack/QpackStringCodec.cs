using System.Buffers;

namespace TurboHTTP.Protocol.Http3.Qpack;

/// <summary>
/// RFC 9204 §4.1.2 — QPACK string literal encoding and decoding.
/// Supports both plain (raw) and Huffman-encoded representations.
/// The high bit of the first byte indicates Huffman encoding (H=1).
/// </summary>
public static class QpackStringCodec
{
    /// <summary>
    /// Encodes a string literal using QPACK representation (RFC 9204 §4.1.2).
    /// Chooses Huffman encoding when it produces shorter output.
    /// Writes directly into the caller-provided span and advances it past the written bytes.
    /// </summary>
    /// <param name="value">The string value to encode.</param>
    /// <param name="prefixBits">Number of bits for the length prefix (typically 7).</param>
    /// <param name="prefixFlags">High bits of the first byte beyond H bit.</param>
    /// <param name="output">Destination span; advanced past the bytes written on return.</param>
    /// <returns>Number of bytes written.</returns>
    public static int Encode(ReadOnlySpan<byte> value, int prefixBits, byte prefixFlags, ref Span<byte> output)
    {
        if (value.IsEmpty)
        {
            // Empty string: H=0, length=0
            return QpackIntegerCodec.Encode(0, prefixBits, prefixFlags, ref output);
        }

        // Check exact Huffman length without allocating
        var huffLen = HuffmanCodec.GetEncodedLength(value);

        if (huffLen < value.Length)
        {
            // Huffman is shorter — encode directly into the output span.
            // Strategy: write integer prefix first, then Huffman data.
            var hBit = (byte)(1 << prefixBits);
            var written = QpackIntegerCodec.Encode(huffLen, prefixBits, (byte)(prefixFlags | hBit), ref output);
            var actualHuffLen = HuffmanCodec.Encode(value, output[..huffLen]);
            output = output[actualHuffLen..];
            return written + actualHuffLen;
        }

        // Plain is shorter or equal — no Huffman.
        var n = QpackIntegerCodec.Encode(value.Length, prefixBits, prefixFlags, ref output);
        value.CopyTo(output);
        output = output[value.Length..];
        return n + value.Length;
    }

    /// <summary>
    /// Encodes a string literal forcing Huffman encoding on or off.
    /// Writes directly into the caller-provided span and advances it past the written bytes.
    /// </summary>
    /// <param name="value">The string value to encode.</param>
    /// <param name="prefixBits">Number of bits for the length prefix (typically 7).</param>
    /// <param name="prefixFlags">High bits of the first byte beyond H bit.</param>
    /// <param name="useHuffman">Whether to force Huffman encoding.</param>
    /// <param name="output">Destination span; advanced past the bytes written on return.</param>
    /// <returns>Number of bytes written.</returns>
    public static int Encode(ReadOnlySpan<byte> value, int prefixBits, byte prefixFlags, bool useHuffman, ref Span<byte> output)
    {
        if (value.IsEmpty)
        {
            var flags = useHuffman ? (byte)(prefixFlags | (1 << prefixBits)) : prefixFlags;
            return QpackIntegerCodec.Encode(0, prefixBits, flags, ref output);
        }

        if (useHuffman)
        {
            var huffLen = HuffmanCodec.GetEncodedLength(value);
            var hBit = (byte)(1 << prefixBits);
            var written = QpackIntegerCodec.Encode(huffLen, prefixBits, (byte)(prefixFlags | hBit), ref output);
            var actualHuffLen = HuffmanCodec.Encode(value, output[..huffLen]);
            output = output[actualHuffLen..];
            return written + actualHuffLen;
        }

        var n = QpackIntegerCodec.Encode(value.Length, prefixBits, prefixFlags, ref output);
        value.CopyTo(output);
        output = output[value.Length..];
        return n + value.Length;
    }

    /// <summary>
    /// Decodes a QPACK string literal from the given data (RFC 9204 §4.1.2).
    /// </summary>
    /// <param name="data">The source data.</param>
    /// <param name="pos">Current read position; advanced past the decoded string.</param>
    /// <param name="prefixBits">Number of bits for the length prefix (typically 7).</param>
    /// <returns>The decoded string as a byte array.</returns>
    public static byte[] Decode(ReadOnlySpan<byte> data, ref int pos, int prefixBits)
    {
        if (pos >= data.Length)
        {
            throw new QpackException("RFC 9204 §4.1.2 violation: Unexpected end of data while reading string literal.");
        }

        var hBit = (byte)(1 << prefixBits);
        var isHuffman = (data[pos] & hBit) != 0;

        var length = QpackIntegerCodec.Decode(data, ref pos, prefixBits);

        if (length == 0)
        {
            return [];
        }

        if (pos + length > data.Length)
        {
            throw new QpackException(
                $"RFC 9204 §4.1.2 violation: String literal length {length} exceeds available data ({data.Length - pos} bytes remaining).");
        }

        var raw = data.Slice(pos, length);
        pos += length;

        if (isHuffman)
        {
            var maxDecoded = HuffmanCodec.GetMaxDecodedLength(raw.Length);
            using var owner = MemoryPool<byte>.Shared.Rent(maxDecoded);
            var decodedLen = HuffmanCodec.Decode(raw, owner.Memory.Span[..maxDecoded]);
            return owner.Memory.Span[..decodedLen].ToArray();
        }

        return raw.ToArray();
    }
}
