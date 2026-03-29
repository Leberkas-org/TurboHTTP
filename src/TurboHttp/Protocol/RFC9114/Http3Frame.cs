using TurboHttp.Protocol.RFC9000;

namespace TurboHttp.Protocol.RFC9114;

// HTTP/3 Frame Types  —  RFC 9114 §7
//
// HTTP/3 Frame Format (RFC 9114 §7.1):
//   +-----------------------------------------------+
//   |            Type (variable-length i)           |
//   +-----------------------------------------------+
//   |           Length (variable-length i)          |
//   +-----------------------------------------------+
//   |          Frame Payload (Length bytes)         |
//   +-----------------------------------------------+
//
// Unlike HTTP/2, HTTP/3 frames have no stream identifier in the
// frame header (QUIC streams provide that) and no flags byte.

public enum Http3FrameType : long
{
    Data = 0x00,
    Headers = 0x01,
    CancelPush = 0x03,
    Settings = 0x04,
    PushPromise = 0x05,
    GoAway = 0x06,
    MaxPushId = 0x0d,
}

public abstract class Http3Frame
{
    public abstract Http3FrameType Type { get; }

    /// <summary>
    /// Total serialized size in bytes including the type and length prefix.
    /// </summary>
    public abstract int SerializedSize { get; }

    /// <summary>
    /// Writes the frame to the destination span and advances it past the written bytes.
    /// Returns the number of bytes written.
    /// </summary>
    public abstract int WriteTo(ref Span<byte> span);

    public byte[] Serialize()
    {
        var buf = new byte[SerializedSize];
        var span = buf.AsSpan();
        WriteTo(ref span);
        return buf;
    }

    /// <summary>
    /// Returns the payload size in bytes (excluding the type/length prefix).
    /// </summary>
    protected abstract int PayloadSize { get; }

    /// <summary>
    /// Writes the QUIC variable-length integer type and length prefix,
    /// then advances the span. Returns the number of prefix bytes written.
    /// </summary>
    protected int WritePrefix(ref Span<byte> span)
    {
        var written = QuicVarInt.Encode((long)Type, span);
        span = span[written..];
        var lengthWritten = QuicVarInt.Encode(PayloadSize, span);
        span = span[lengthWritten..];
        return written + lengthWritten;
    }

    /// <summary>
    /// Size of the type + length prefix in bytes.
    /// </summary>
    protected int PrefixSize => QuicVarInt.EncodedLength((long)Type) + QuicVarInt.EncodedLength(PayloadSize);
}

/// <summary>
/// DATA frame (RFC 9114 §7.2.1).
/// Carries request or response body data on a request stream.
/// </summary>
public sealed class Http3DataFrame : Http3Frame
{
    public override Http3FrameType Type => Http3FrameType.Data;
    public ReadOnlyMemory<byte> Data { get; }

    public Http3DataFrame(ReadOnlyMemory<byte> data)
    {
        Data = data;
    }

    protected override int PayloadSize => Data.Length;
    public override int SerializedSize => PrefixSize + Data.Length;

    public override int WriteTo(ref Span<byte> span)
    {
        var size = SerializedSize;
        WritePrefix(ref span);
        Data.Span.CopyTo(span);
        span = span[Data.Length..];
        return size;
    }
}

/// <summary>
/// HEADERS frame (RFC 9114 §7.2.2).
/// Carries a compressed QPACK header block on a request stream.
/// </summary>
public sealed class Http3HeadersFrame : Http3Frame
{
    public override Http3FrameType Type => Http3FrameType.Headers;
    public ReadOnlyMemory<byte> HeaderBlock { get; }

    public Http3HeadersFrame(ReadOnlyMemory<byte> headerBlock)
    {
        HeaderBlock = headerBlock;
    }

    protected override int PayloadSize => HeaderBlock.Length;
    public override int SerializedSize => PrefixSize + HeaderBlock.Length;

    public override int WriteTo(ref Span<byte> span)
    {
        var size = SerializedSize;
        WritePrefix(ref span);
        HeaderBlock.Span.CopyTo(span);
        span = span[HeaderBlock.Length..];
        return size;
    }
}

/// <summary>
/// CANCEL_PUSH frame (RFC 9114 §7.2.3).
/// Requests cancellation of a server push before the push stream is received.
/// Sent on the control stream. Payload is a single QUIC variable-length push ID.
/// </summary>
public sealed class Http3CancelPushFrame : Http3Frame
{
    public override Http3FrameType Type => Http3FrameType.CancelPush;
    public long PushId { get; }

    public Http3CancelPushFrame(long pushId)
    {
        if (pushId < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pushId), pushId, "Push ID must be non-negative.");
        }

        PushId = pushId;
    }

    protected override int PayloadSize => QuicVarInt.EncodedLength(PushId);
    public override int SerializedSize => PrefixSize + PayloadSize;

    public override int WriteTo(ref Span<byte> span)
    {
        var size = SerializedSize;
        WritePrefix(ref span);
        var written = QuicVarInt.Encode(PushId, span);
        span = span[written..];
        return size;
    }
}

/// <summary>
/// SETTINGS frame (RFC 9114 §7.2.4).
/// Conveys configuration parameters on the control stream.
/// Each parameter is an identifier-value pair of QUIC variable-length integers.
/// Unlike HTTP/2, there is no ACK mechanism — the transport provides reliability.
/// </summary>
public sealed class Http3SettingsFrame : Http3Frame
{
    public override Http3FrameType Type => Http3FrameType.Settings;
    public IReadOnlyList<(long Identifier, long Value)> Parameters { get; }

    public Http3SettingsFrame(IReadOnlyList<(long Identifier, long Value)> parameters)
    {
        Parameters = parameters;
    }

    protected override int PayloadSize
    {
        get
        {
            var size = 0;
            foreach (var (id, val) in Parameters)
            {
                size += QuicVarInt.EncodedLength(id) + QuicVarInt.EncodedLength(val);
            }

            return size;
        }
    }

    public override int SerializedSize => PrefixSize + PayloadSize;

    public override int WriteTo(ref Span<byte> span)
    {
        var size = SerializedSize;
        WritePrefix(ref span);

        foreach (var (id, val) in Parameters)
        {
            var written = QuicVarInt.Encode(id, span);
            span = span[written..];
            written = QuicVarInt.Encode(val, span);
            span = span[written..];
        }

        return size;
    }
}

/// <summary>
/// PUSH_PROMISE frame (RFC 9114 §7.2.5).
/// Carries a push ID followed by a compressed QPACK header block on a request stream.
/// </summary>
public sealed class Http3PushPromiseFrame : Http3Frame
{
    public override Http3FrameType Type => Http3FrameType.PushPromise;
    public long PushId { get; }
    public ReadOnlyMemory<byte> HeaderBlock { get; }

    public Http3PushPromiseFrame(long pushId, ReadOnlyMemory<byte> headerBlock)
    {
        if (pushId < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pushId), pushId, "Push ID must be non-negative.");
        }

        PushId = pushId;
        HeaderBlock = headerBlock;
    }

    protected override int PayloadSize => QuicVarInt.EncodedLength(PushId) + HeaderBlock.Length;
    public override int SerializedSize => PrefixSize + PayloadSize;

    public override int WriteTo(ref Span<byte> span)
    {
        var size = SerializedSize;
        WritePrefix(ref span);
        var written = QuicVarInt.Encode(PushId, span);
        span = span[written..];
        HeaderBlock.Span.CopyTo(span);
        span = span[HeaderBlock.Length..];
        return size;
    }
}

/// <summary>
/// GOAWAY frame (RFC 9114 §7.2.6).
/// Initiates graceful shutdown of a connection. Payload is a single QUIC
/// variable-length integer indicating the stream ID or push ID.
/// </summary>
public sealed class Http3GoAwayFrame : Http3Frame
{
    public override Http3FrameType Type => Http3FrameType.GoAway;
    public long StreamId { get; }

    public Http3GoAwayFrame(long streamId)
    {
        if (streamId < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(streamId), streamId, "Stream/Push ID must be non-negative.");
        }

        StreamId = streamId;
    }

    protected override int PayloadSize => QuicVarInt.EncodedLength(StreamId);
    public override int SerializedSize => PrefixSize + PayloadSize;

    public override int WriteTo(ref Span<byte> span)
    {
        var size = SerializedSize;
        WritePrefix(ref span);
        var written = QuicVarInt.Encode(StreamId, span);
        span = span[written..];
        return size;
    }
}

/// <summary>
/// MAX_PUSH_ID frame (RFC 9114 §7.2.7).
/// Sent by the client on the control stream to indicate the maximum push ID
/// the server is permitted to use.
/// </summary>
public sealed class Http3MaxPushIdFrame : Http3Frame
{
    public override Http3FrameType Type => Http3FrameType.MaxPushId;
    public long PushId { get; }

    public Http3MaxPushIdFrame(long pushId)
    {
        if (pushId < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pushId), pushId, "Push ID must be non-negative.");
        }

        PushId = pushId;
    }

    protected override int PayloadSize => QuicVarInt.EncodedLength(PushId);
    public override int SerializedSize => PrefixSize + PayloadSize;

    public override int WriteTo(ref Span<byte> span)
    {
        var size = SerializedSize;
        WritePrefix(ref span);
        var written = QuicVarInt.Encode(PushId, span);
        span = span[written..];
        return size;
    }
}
