using System;
using System.Buffers.Binary;

namespace TurboHttp.Protocol.RFC9000;

/// <summary>
/// QUIC variable-length integer encoding and decoding per RFC 9000 §16.
/// Supports values from 0 to 2^62 - 1 using 1, 2, 4, or 8 bytes.
/// The two most-significant bits of the first byte encode the length prefix.
/// </summary>
public static class QuicVarInt
{
    /// <summary>Maximum encodable value: 2^62 - 1.</summary>
    public const long MaxValue = (1L << 62) - 1;

    private const long OneByteMax = 63;               // 2^6 - 1
    private const long TwoByteMax = 16383;             // 2^14 - 1
    private const long FourByteMax = 1073741823;       // 2^30 - 1

    /// <summary>
    /// Returns the number of bytes needed to encode <paramref name="value"/>.
    /// </summary>
    public static int EncodedLength(long value)
    {
        if ((ulong)value > MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Value exceeds QUIC variable-length integer maximum (2^62 - 1).");
        }

        if (value <= OneByteMax)
        {
            return 1;
        }

        if (value <= TwoByteMax)
        {
            return 2;
        }

        if (value <= FourByteMax)
        {
            return 4;
        }

        return 8;
    }

    /// <summary>
    /// Encodes <paramref name="value"/> into <paramref name="destination"/>.
    /// Returns the number of bytes written (1, 2, 4, or 8).
    /// </summary>
    public static int Encode(long value, Span<byte> destination)
    {
        if ((ulong)value > MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Value exceeds QUIC variable-length integer maximum (2^62 - 1).");
        }

        if (value <= OneByteMax)
        {
            if (destination.Length < 1)
            {
                throw new ArgumentException("Destination too small.", nameof(destination));
            }

            destination[0] = (byte)value; // 2MSB = 00
            return 1;
        }

        if (value <= TwoByteMax)
        {
            if (destination.Length < 2)
            {
                throw new ArgumentException("Destination too small.", nameof(destination));
            }

            BinaryPrimitives.WriteUInt16BigEndian(destination, (ushort)(0x4000 | value));
            return 2;
        }

        if (value <= FourByteMax)
        {
            if (destination.Length < 4)
            {
                throw new ArgumentException("Destination too small.", nameof(destination));
            }

            BinaryPrimitives.WriteUInt32BigEndian(destination, (uint)(0x80000000 | value));
            return 4;
        }

        if (destination.Length < 8)
        {
            throw new ArgumentException("Destination too small.", nameof(destination));
        }

        BinaryPrimitives.WriteUInt64BigEndian(destination, 0xC000000000000000UL | (ulong)value);
        return 8;
    }

    /// <summary>
    /// Tries to decode a variable-length integer from <paramref name="source"/>.
    /// Returns <c>true</c> if successful, with <paramref name="value"/> and <paramref name="bytesConsumed"/> set.
    /// Returns <c>false</c> if <paramref name="source"/> is too short.
    /// </summary>
    public static bool TryDecode(ReadOnlySpan<byte> source, out long value, out int bytesConsumed)
    {
        value = 0;
        bytesConsumed = 0;

        if (source.Length < 1)
        {
            return false;
        }

        var prefix = source[0] >> 6;
        var length = 1 << prefix; // 1, 2, 4, or 8

        if (source.Length < length)
        {
            return false;
        }

        switch (length)
        {
            case 1:
                value = source[0] & 0x3F;
                break;
            case 2:
                value = BinaryPrimitives.ReadUInt16BigEndian(source) & 0x3FFF;
                break;
            case 4:
                value = BinaryPrimitives.ReadUInt32BigEndian(source) & 0x3FFFFFFF;
                break;
            case 8:
                value = (long)(BinaryPrimitives.ReadUInt64BigEndian(source) & 0x3FFFFFFFFFFFFFFFUL);
                break;
        }

        bytesConsumed = length;
        return true;
    }

    /// <summary>
    /// Decodes a variable-length integer from <paramref name="source"/>.
    /// Throws if the source is too short.
    /// </summary>
    public static long Decode(ReadOnlySpan<byte> source, out int bytesConsumed)
    {
        if (!TryDecode(source, out var value, out bytesConsumed))
        {
            throw new ArgumentException("Source buffer is too short to decode a QUIC variable-length integer.", nameof(source));
        }

        return value;
    }
}
