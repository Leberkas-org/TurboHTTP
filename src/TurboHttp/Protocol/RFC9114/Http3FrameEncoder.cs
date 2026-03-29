using System.Buffers;
using TurboHttp.Protocol.RFC9000;

namespace TurboHttp.Protocol.RFC9114;

/// <summary>
/// Encodes HTTP/3 frames to wire format per RFC 9114 §7.
/// Provides zero-copy encoding to <see cref="IBufferWriter{T}"/> and <see cref="Span{T}"/>
/// targets, plus direct encoding methods that skip frame object allocation.
/// </summary>
public static class Http3FrameEncoder
{
    /// <summary>
    /// Encodes a frame to the destination span.
    /// Returns the number of bytes written.
    /// </summary>
    public static int Encode(Http3Frame frame, Span<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (destination.Length < frame.SerializedSize)
        {
            throw new ArgumentException(
                $"Destination too small. Need {frame.SerializedSize} bytes, got {destination.Length}.",
                nameof(destination));
        }

        var span = destination;
        return frame.WriteTo(ref span);
    }

    /// <summary>
    /// Encodes a frame to the buffer writer.
    /// Returns the number of bytes written.
    /// </summary>
    public static int Encode(Http3Frame frame, IBufferWriter<byte> writer)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(writer);

        var size = frame.SerializedSize;
        var span = writer.GetSpan(size);
        var written = frame.WriteTo(ref span);
        writer.Advance(written);
        return written;
    }

    /// <summary>
    /// Encodes multiple frames to the buffer writer.
    /// Returns the total number of bytes written.
    /// </summary>
    public static int EncodeAll(IEnumerable<Http3Frame> frames, IBufferWriter<byte> writer)
    {
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentNullException.ThrowIfNull(writer);

        var total = 0;
        foreach (var frame in frames)
        {
            total += Encode(frame, writer);
        }

        return total;
    }

    // ─────────────── Direct encoding (no frame allocation) ───────────────

    /// <summary>
    /// Encodes a DATA frame directly to the destination span.
    /// Returns the number of bytes written.
    /// </summary>
    public static int EncodeData(ReadOnlySpan<byte> payload, Span<byte> destination)
    {
        var payloadSize = payload.Length;
        var prefixSize = QuicVarInt.EncodedLength((long)Http3FrameType.Data) + QuicVarInt.EncodedLength(payloadSize);
        var totalSize = prefixSize + payloadSize;

        if (destination.Length < totalSize)
        {
            throw new ArgumentException(
                $"Destination too small. Need {totalSize} bytes, got {destination.Length}.",
                nameof(destination));
        }

        var span = destination;
        var written = QuicVarInt.Encode((long)Http3FrameType.Data, span);
        span = span[written..];
        written = QuicVarInt.Encode(payloadSize, span);
        span = span[written..];
        payload.CopyTo(span);

        return totalSize;
    }

    /// <summary>
    /// Encodes a HEADERS frame directly to the destination span.
    /// Returns the number of bytes written.
    /// </summary>
    public static int EncodeHeaders(ReadOnlySpan<byte> headerBlock, Span<byte> destination)
    {
        var payloadSize = headerBlock.Length;
        var prefixSize = QuicVarInt.EncodedLength((long)Http3FrameType.Headers) + QuicVarInt.EncodedLength(payloadSize);
        var totalSize = prefixSize + payloadSize;

        if (destination.Length < totalSize)
        {
            throw new ArgumentException(
                $"Destination too small. Need {totalSize} bytes, got {destination.Length}.",
                nameof(destination));
        }

        var span = destination;
        var written = QuicVarInt.Encode((long)Http3FrameType.Headers, span);
        span = span[written..];
        written = QuicVarInt.Encode(payloadSize, span);
        span = span[written..];
        headerBlock.CopyTo(span);

        return totalSize;
    }

    /// <summary>
    /// Encodes a CANCEL_PUSH frame directly to the destination span.
    /// Returns the number of bytes written.
    /// </summary>
    public static int EncodeCancelPush(long pushId, Span<byte> destination)
    {
        if (pushId < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pushId), pushId, "Push ID must be non-negative.");
        }

        var payloadSize = QuicVarInt.EncodedLength(pushId);
        var prefixSize = QuicVarInt.EncodedLength((long)Http3FrameType.CancelPush) + QuicVarInt.EncodedLength(payloadSize);
        var totalSize = prefixSize + payloadSize;

        if (destination.Length < totalSize)
        {
            throw new ArgumentException(
                $"Destination too small. Need {totalSize} bytes, got {destination.Length}.",
                nameof(destination));
        }

        var span = destination;
        var written = QuicVarInt.Encode((long)Http3FrameType.CancelPush, span);
        span = span[written..];
        written = QuicVarInt.Encode(payloadSize, span);
        span = span[written..];
        QuicVarInt.Encode(pushId, span);

        return totalSize;
    }

    /// <summary>
    /// Encodes a SETTINGS frame directly to the destination span.
    /// Returns the number of bytes written.
    /// </summary>
    public static int EncodeSettings(IReadOnlyList<(long Identifier, long Value)> parameters, Span<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var payloadSize = 0;
        foreach (var (id, val) in parameters)
        {
            payloadSize += QuicVarInt.EncodedLength(id) + QuicVarInt.EncodedLength(val);
        }

        var prefixSize = QuicVarInt.EncodedLength((long)Http3FrameType.Settings) + QuicVarInt.EncodedLength(payloadSize);
        var totalSize = prefixSize + payloadSize;

        if (destination.Length < totalSize)
        {
            throw new ArgumentException(
                $"Destination too small. Need {totalSize} bytes, got {destination.Length}.",
                nameof(destination));
        }

        var span = destination;
        var written = QuicVarInt.Encode((long)Http3FrameType.Settings, span);
        span = span[written..];
        written = QuicVarInt.Encode(payloadSize, span);
        span = span[written..];

        foreach (var (id, val) in parameters)
        {
            written = QuicVarInt.Encode(id, span);
            span = span[written..];
            written = QuicVarInt.Encode(val, span);
            span = span[written..];
        }

        return totalSize;
    }

    /// <summary>
    /// Encodes a PUSH_PROMISE frame directly to the destination span.
    /// Returns the number of bytes written.
    /// </summary>
    public static int EncodePushPromise(long pushId, ReadOnlySpan<byte> headerBlock, Span<byte> destination)
    {
        if (pushId < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pushId), pushId, "Push ID must be non-negative.");
        }

        var payloadSize = QuicVarInt.EncodedLength(pushId) + headerBlock.Length;
        var prefixSize = QuicVarInt.EncodedLength((long)Http3FrameType.PushPromise) + QuicVarInt.EncodedLength(payloadSize);
        var totalSize = prefixSize + payloadSize;

        if (destination.Length < totalSize)
        {
            throw new ArgumentException(
                $"Destination too small. Need {totalSize} bytes, got {destination.Length}.",
                nameof(destination));
        }

        var span = destination;
        var written = QuicVarInt.Encode((long)Http3FrameType.PushPromise, span);
        span = span[written..];
        written = QuicVarInt.Encode(payloadSize, span);
        span = span[written..];
        written = QuicVarInt.Encode(pushId, span);
        span = span[written..];
        headerBlock.CopyTo(span);

        return totalSize;
    }

    /// <summary>
    /// Encodes a GOAWAY frame directly to the destination span.
    /// Returns the number of bytes written.
    /// </summary>
    public static int EncodeGoAway(long streamId, Span<byte> destination)
    {
        if (streamId < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(streamId), streamId, "Stream/Push ID must be non-negative.");
        }

        var payloadSize = QuicVarInt.EncodedLength(streamId);
        var prefixSize = QuicVarInt.EncodedLength((long)Http3FrameType.GoAway) + QuicVarInt.EncodedLength(payloadSize);
        var totalSize = prefixSize + payloadSize;

        if (destination.Length < totalSize)
        {
            throw new ArgumentException(
                $"Destination too small. Need {totalSize} bytes, got {destination.Length}.",
                nameof(destination));
        }

        var span = destination;
        var written = QuicVarInt.Encode((long)Http3FrameType.GoAway, span);
        span = span[written..];
        written = QuicVarInt.Encode(payloadSize, span);
        span = span[written..];
        QuicVarInt.Encode(streamId, span);

        return totalSize;
    }

    /// <summary>
    /// Encodes a MAX_PUSH_ID frame directly to the destination span.
    /// Returns the number of bytes written.
    /// </summary>
    public static int EncodeMaxPushId(long pushId, Span<byte> destination)
    {
        if (pushId < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pushId), pushId, "Push ID must be non-negative.");
        }

        var payloadSize = QuicVarInt.EncodedLength(pushId);
        var prefixSize = QuicVarInt.EncodedLength((long)Http3FrameType.MaxPushId) + QuicVarInt.EncodedLength(payloadSize);
        var totalSize = prefixSize + payloadSize;

        if (destination.Length < totalSize)
        {
            throw new ArgumentException(
                $"Destination too small. Need {totalSize} bytes, got {destination.Length}.",
                nameof(destination));
        }

        var span = destination;
        var written = QuicVarInt.Encode((long)Http3FrameType.MaxPushId, span);
        span = span[written..];
        written = QuicVarInt.Encode(payloadSize, span);
        span = span[written..];
        QuicVarInt.Encode(pushId, span);

        return totalSize;
    }
}
