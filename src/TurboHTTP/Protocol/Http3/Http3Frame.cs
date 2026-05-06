using System.Buffers;

namespace TurboHTTP.Protocol.Http3;

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

internal enum FrameType : long
{
    Data = 0x00,
    Headers = 0x01,
    CancelPush = 0x03,
    Settings = 0x04,
    PushPromise = 0x05,
    GoAway = 0x06,
    MaxPushId = 0x0d,
}

internal abstract class Http3Frame
{
    public abstract FrameType Type { get; }

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
internal sealed class DataFrame : Http3Frame, IDisposable
{
    private readonly IMemoryOwner<byte>? _owner;

    public override FrameType Type => FrameType.Data;
    public ReadOnlyMemory<byte> Data { get; }

    public DataFrame(ReadOnlyMemory<byte> data)
    {
        Data = data;
    }

    internal DataFrame(IMemoryOwner<byte> owner, int length)
    {
        _owner = owner;
        Data = owner.Memory[..length];
    }

    public void Dispose() => _owner?.Dispose();

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
internal sealed class HeadersFrame : Http3Frame, IDisposable
{
    private readonly IMemoryOwner<byte>? _owner;

    public override FrameType Type => FrameType.Headers;
    public ReadOnlyMemory<byte> HeaderBlock { get; }

    public HeadersFrame(ReadOnlyMemory<byte> headerBlock)
    {
        HeaderBlock = headerBlock;
    }

    internal HeadersFrame(IMemoryOwner<byte> owner, int length)
    {
        _owner = owner;
        HeaderBlock = owner.Memory[..length];
    }

    public void Dispose() => _owner?.Dispose();

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
internal sealed class CancelPushFrame : Http3Frame
{
    public override FrameType Type => FrameType.CancelPush;
    public long PushId { get; }

    public CancelPushFrame(long pushId)
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
internal sealed class SettingsFrame : Http3Frame
{
    public override FrameType Type => FrameType.Settings;
    public IReadOnlyList<(long Identifier, long Value)> Parameters { get; }

    public SettingsFrame(IReadOnlyList<(long Identifier, long Value)> parameters)
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
internal sealed class PushPromiseFrame : Http3Frame, IDisposable
{
    private readonly IMemoryOwner<byte>? _owner;

    public override FrameType Type => FrameType.PushPromise;
    public long PushId { get; }
    public ReadOnlyMemory<byte> HeaderBlock { get; }

    public PushPromiseFrame(long pushId, ReadOnlyMemory<byte> headerBlock)
    {
        if (pushId < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pushId), pushId, "Push ID must be non-negative.");
        }

        PushId = pushId;
        HeaderBlock = headerBlock;
    }

    internal PushPromiseFrame(long pushId, IMemoryOwner<byte> owner, int length)
    {
        if (pushId < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pushId), pushId, "Push ID must be non-negative.");
        }

        PushId = pushId;
        _owner = owner;
        HeaderBlock = owner.Memory[..length];
    }

    public void Dispose() => _owner?.Dispose();

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
internal sealed class GoAwayFrame : Http3Frame
{
    public override FrameType Type => FrameType.GoAway;
    public long StreamId { get; }

    public GoAwayFrame(long streamId)
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
internal sealed class MaxPushIdFrame : Http3Frame
{
    public override FrameType Type => FrameType.MaxPushId;
    public long PushId { get; }

    public MaxPushIdFrame(long pushId)
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
/// Well-known HTTP/3 setting identifiers per RFC 9114 §7.2.4.1.
/// Analogous to HTTP/2's <see cref="Http2.SettingsParameter"/> enum.
/// </summary>
internal static class SettingsIdentifier
{
    /// <summary>
    /// SETTINGS_QPACK_MAX_TABLE_CAPACITY (RFC 9204 §5).
    /// Maximum size the QPACK dynamic table can reach. Default: 0.
    /// </summary>
    public const long QpackMaxTableCapacity = 0x01;

    /// <summary>
    /// Reserved identifier — corresponds to HTTP/2 SETTINGS_ENABLE_PUSH.
    /// MUST NOT be sent in HTTP/3 (RFC 9114 §7.2.4.1).
    /// </summary>
    public const long ReservedH2EnablePush = 0x02;

    /// <summary>
    /// Reserved identifier — corresponds to HTTP/2 SETTINGS_MAX_CONCURRENT_STREAMS.
    /// MUST NOT be sent in HTTP/3 (RFC 9114 §7.2.4.1).
    /// </summary>
    public const long ReservedH2MaxConcurrentStreams = 0x03;

    /// <summary>
    /// Reserved identifier — corresponds to HTTP/2 SETTINGS_INITIAL_WINDOW_SIZE.
    /// MUST NOT be sent in HTTP/3 (RFC 9114 §7.2.4.1).
    /// </summary>
    public const long ReservedH2InitialWindowSize = 0x04;

    /// <summary>
    /// Reserved identifier — corresponds to HTTP/2 SETTINGS_MAX_FRAME_SIZE.
    /// MUST NOT be sent in HTTP/3 (RFC 9114 §7.2.4.1).
    /// </summary>
    public const long ReservedH2MaxFrameSize = 0x05;

    /// <summary>
    /// SETTINGS_MAX_FIELD_SECTION_SIZE (RFC 9114 §7.2.4.1).
    /// Advisory maximum size of a header block the peer is prepared to accept.
    /// </summary>
    public const long MaxFieldSectionSize = 0x06;

    /// <summary>
    /// SETTINGS_QPACK_BLOCKED_STREAMS (RFC 9204 §5).
    /// Maximum number of streams that can be blocked waiting for QPACK. Default: 0.
    /// </summary>
    public const long QpackBlockedStreams = 0x07;

    /// <summary>
    /// Returns true if the identifier is reserved (corresponds to an HTTP/2 setting
    /// that MUST NOT be sent in HTTP/3 per RFC 9114 §7.2.4.1).
    /// </summary>
    public static bool IsReservedH2Setting(long identifier) =>
        identifier is ReservedH2EnablePush
            or ReservedH2MaxConcurrentStreams
            or ReservedH2InitialWindowSize
            or ReservedH2MaxFrameSize;

    /// <summary>
    /// Validates that a list of setting parameters does not contain HTTP/2-specific
    /// identifiers (RFC 9114 §7.2.4.1). This can be used for pre-validation of
    /// raw payloads before deserialization.
    /// </summary>
    /// <param name="parameters">The setting identifier-value pairs to validate.</param>
    /// <exception cref="Http3Exception">
    /// Thrown if any parameter uses a reserved HTTP/2 identifier.
    /// </exception>
    public static void RejectForbiddenH2Settings(IReadOnlyList<(long Identifier, long Value)> parameters)
    {
        for (var i = 0; i < parameters.Count; i++)
        {
            var (id, _) = parameters[i];
            if (IsReservedH2Setting(id))
            {
                throw new Http3Exception(ErrorCode.SettingsError,
                    $"Setting identifier 0x{id:x2} is a reserved HTTP/2 setting and MUST NOT appear in HTTP/3 (RFC 9114 §7.2.4.1).");
            }
        }
    }
}
