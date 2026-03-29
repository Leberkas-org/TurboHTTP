using System.Buffers;

namespace TurboHttp.Protocol.RFC9204;

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
    /// </summary>
    /// <param name="value">The string value to encode.</param>
    /// <param name="prefixBits">Number of bits for the length prefix (typically 7).</param>
    /// <param name="prefixFlags">High bits of the first byte beyond H bit.</param>
    /// <param name="output">Destination buffer writer.</param>
    public static void Encode(ReadOnlySpan<byte> value, int prefixBits, byte prefixFlags, IBufferWriter<byte> output)
    {
        if (value.IsEmpty)
        {
            // Empty string: H=0, length=0
            QpackIntegerCodec.Encode(0, prefixBits, prefixFlags, output);
            return;
        }

        var huffmanEncoded = HuffmanCodec.Encode(value);

        if (huffmanEncoded.Length < value.Length)
        {
            // Huffman is shorter — use it. Set H bit (top of prefix byte).
            var hBit = (byte)(1 << prefixBits);
            QpackIntegerCodec.Encode(huffmanEncoded.Length, prefixBits, (byte)(prefixFlags | hBit), output);
            var span = output.GetSpan(huffmanEncoded.Length);
            huffmanEncoded.AsSpan().CopyTo(span);
            output.Advance(huffmanEncoded.Length);
        }
        else
        {
            // Plain is shorter or equal — no Huffman.
            QpackIntegerCodec.Encode(value.Length, prefixBits, prefixFlags, output);
            var span = output.GetSpan(value.Length);
            value.CopyTo(span);
            output.Advance(value.Length);
        }
    }

    /// <summary>
    /// Encodes a string literal forcing Huffman encoding on or off.
    /// </summary>
    public static void Encode(ReadOnlySpan<byte> value, int prefixBits, byte prefixFlags, bool useHuffman, IBufferWriter<byte> output)
    {
        if (value.IsEmpty)
        {
            var flags = useHuffman ? (byte)(prefixFlags | (1 << prefixBits)) : prefixFlags;
            QpackIntegerCodec.Encode(0, prefixBits, flags, output);
            return;
        }

        if (useHuffman)
        {
            var huffmanEncoded = HuffmanCodec.Encode(value);
            var hBit = (byte)(1 << prefixBits);
            QpackIntegerCodec.Encode(huffmanEncoded.Length, prefixBits, (byte)(prefixFlags | hBit), output);
            var span = output.GetSpan(huffmanEncoded.Length);
            huffmanEncoded.AsSpan().CopyTo(span);
            output.Advance(huffmanEncoded.Length);
        }
        else
        {
            QpackIntegerCodec.Encode(value.Length, prefixBits, prefixFlags, output);
            var span = output.GetSpan(value.Length);
            value.CopyTo(span);
            output.Advance(value.Length);
        }
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
            return HuffmanCodec.Decode(raw);
        }

        return raw.ToArray();
    }
}
