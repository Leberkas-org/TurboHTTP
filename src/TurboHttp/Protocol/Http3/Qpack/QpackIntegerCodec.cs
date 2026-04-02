using System.Buffers;

namespace TurboHttp.Protocol.Http3.Qpack;

/// <summary>
/// RFC 9204 §4.1.1 — QPACK integer encoding and decoding.
/// Uses the same variable-length integer representation as HPACK (RFC 7541 §5.1)
/// with support for prefix lengths from 1 to 8 bits.
/// </summary>
public static class QpackIntegerCodec
{
    /// <summary>
    /// Maximum integer value accepted to prevent overflow attacks.
    /// </summary>
    private const int MaxIntegerValue = int.MaxValue >> 1;

    /// <summary>
    /// Encodes a non-negative integer using QPACK integer representation (RFC 9204 §4.1.1).
    /// </summary>
    /// <param name="value">The integer value to encode (must be non-negative).</param>
    /// <param name="prefixBits">Number of bits available in the first byte (1–8).</param>
    /// <param name="prefixFlags">High bits of the first byte (the representation type flags).</param>
    /// <param name="output">Destination buffer writer.</param>
    public static void Encode(int value, int prefixBits, byte prefixFlags, IBufferWriter<byte> output)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Value must be non-negative.");
        }

        if (prefixBits is < 1 or > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(prefixBits), "prefixBits must be between 1 and 8.");
        }

        var mask = (1 << prefixBits) - 1;

        if (value < mask)
        {
            var span = output.GetSpan(1);
            span[0] = (byte)(prefixFlags | value);
            output.Advance(1);
            return;
        }

        // Value does not fit — emit prefix byte followed by continuation bytes
        var firstSpan = output.GetSpan(1);
        firstSpan[0] = (byte)(prefixFlags | mask);
        output.Advance(1);

        var remaining = value - mask;

        while (remaining >= 0x80)
        {
            var contSpan = output.GetSpan(1);
            contSpan[0] = (byte)((remaining & 0x7F) | 0x80);
            output.Advance(1);
            remaining >>= 7;
        }

        var lastSpan = output.GetSpan(1);
        lastSpan[0] = (byte)remaining;
        output.Advance(1);
    }

    /// <summary>
    /// Decodes a QPACK integer from the given data (RFC 9204 §4.1.1).
    /// </summary>
    /// <param name="data">The source data.</param>
    /// <param name="pos">Current read position; advanced past the decoded integer.</param>
    /// <param name="prefixBits">Number of prefix bits in the first byte (1–8).</param>
    /// <returns>The decoded integer value.</returns>
    public static int Decode(ReadOnlySpan<byte> data, ref int pos, int prefixBits)
    {
        if (prefixBits is < 1 or > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(prefixBits), "prefixBits must be between 1 and 8.");
        }

        if (pos >= data.Length)
        {
            throw new QpackException("RFC 9204 §4.1.1 violation: Unexpected end of data while reading integer.");
        }

        var mask = (1 << prefixBits) - 1;
        var value = data[pos] & mask;
        pos++;

        if (value < mask)
        {
            return value;
        }

        // Multi-byte integer decoding
        var shift = 0;
        long lvalue = value;

        while (true)
        {
            if (pos >= data.Length)
            {
                throw new QpackException("RFC 9204 §4.1.1 violation: Integer is truncated (no stop bit found).");
            }

            if (shift >= 62)
            {
                throw new QpackException("RFC 9204 §4.1.1 violation: Integer overflow - encoding length exceeded.");
            }

            var b = data[pos++];
            lvalue += (long)(b & 0x7F) << shift;
            shift += 7;

            if (lvalue > MaxIntegerValue)
            {
                throw new QpackException(
                    $"RFC 9204 §4.1.1 violation: Integer overflow - value {lvalue} exceeds maximum {MaxIntegerValue}.");
            }

            if ((b & 0x80) == 0)
            {
                return (int)lvalue;
            }
        }
    }
}
