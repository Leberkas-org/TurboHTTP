using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace TurboHttp.Protocol;

// ══════════════════════════════════════════════════════════════════════════════
// HTTP/2 Frame Types  —  RFC 7540 §6
//
// Frame-Header (9 Bytes, RFC 7540 §4.1):
//   +-----------------------------------------------+
//   |                 Length (24)                    |
//   +---------------+---------------+---------------+
//   |   Type (8)    |   Flags (8)   |
//   +-+-------------+---------------+-------------------------------+
//   |R|                 Stream Identifier (31)                      |
//   +=+=============================================================+
//   |                   Frame Payload (0...)                        |
//   +---------------------------------------------------------------+
// ══════════════════════════════════════════════════════════════════════════════
public enum FrameType : byte
{
    Data = 0x0,
    Headers = 0x1,
    Priority = 0x2,
    RstStream = 0x3,
    Settings = 0x4,
    PushPromise = 0x5,
    Ping = 0x6,
    GoAway = 0x7,
    WindowUpdate = 0x8,
    Continuation = 0x9,
}

[Flags]
public enum DataFlags : byte
{
    None = 0x0,
    EndStream = 0x1,
    Padded = 0x8,
}

[Flags]
public enum Headers : byte
{
    None = 0x0,
    EndStream = 0x1,
    EndHeaders = 0x4,
    Padded = 0x8,
    Priority = 0x20,
}

[Flags]
public enum Settings : byte
{
    None = 0x0,
    Ack = 0x1,
}

[Flags]
public enum PingFlags : byte
{
    None = 0x0,
    Ack = 0x1,
}

[Flags]
public enum ContinuationFlags : byte
{
    None = 0x0,
    EndHeaders = 0x4,
}

public enum SettingsParameter : ushort
{
    HeaderTableSize = 0x1,
    EnablePush = 0x2,
    MaxConcurrentStreams = 0x3,
    InitialWindowSize = 0x4,
    MaxFrameSize = 0x5,
    MaxHeaderListSize = 0x6,
}

public enum Http2ErrorCode : uint
{
    NoError = 0x0,
    ProtocolError = 0x1,
    InternalError = 0x2,
    FlowControlError = 0x3,
    SettingsTimeout = 0x4,
    StreamClosed = 0x5,
    FrameSizeError = 0x6,
    RefusedStream = 0x7,
    Cancel = 0x8,
    CompressionError = 0x9,
    ConnectError = 0xa,
    EnhanceYourCalm = 0xb,
    InadequateSecurity = 0xc,
    Http11Required = 0xd,
}

public abstract class Http2Frame(int streamId)
{
    public int StreamId { get; } = streamId;
    public abstract FrameType Type { get; }

    public abstract int SerializedSize { get; }

    public abstract int WriteTo(ref Span<byte> span);

    public byte[] Serialize()
    {
        var buf = new byte[SerializedSize];
        var span = buf.AsSpan();
        WriteTo(ref span);
        return buf;
    }

    protected static void WriteHeader(ref Span<byte> span, int payloadLength, FrameType type, byte flags, int streamId)
    {
        span[0] = (byte)(payloadLength >> 16);
        span[1] = (byte)(payloadLength >> 8);
        span[2] = (byte)payloadLength;
        span[3] = (byte)type;
        span[4] = flags;
        BinaryPrimitives.WriteUInt32BigEndian(span[5..], (uint)streamId & 0x7FFFFFFFu);
    }

    protected const int FrameHeaderSize = 9;
}

public sealed class DataFrame : Http2Frame
{
    public override FrameType Type => FrameType.Data;
    public ReadOnlyMemory<byte> Data { get; }
    public bool EndStream { get; }

    public DataFrame(int streamId, ReadOnlyMemory<byte> data, bool endStream = false) : base(streamId)
    {
        Data = data;
        EndStream = endStream;
    }

    public override int SerializedSize => FrameHeaderSize + Data.Length;

    public override int WriteTo(ref Span<byte> span)
    {
        var flags = EndStream ? (byte)DataFlags.EndStream : (byte)DataFlags.None;
        WriteHeader(ref span, Data.Length, FrameType.Data, flags, StreamId);
        span = span[FrameHeaderSize..];
        Data.Span.CopyTo(span);
        span = span[Data.Length..];
        return SerializedSize;
    }
}

public sealed class HeadersFrame : Http2Frame
{
    public override FrameType Type => FrameType.Headers;
    public ReadOnlyMemory<byte> HeaderBlockFragment { get; }
    public bool EndStream { get; }
    public bool EndHeaders { get; }

    public HeadersFrame(int streamId, ReadOnlyMemory<byte> headerBlock, bool endStream = false, bool endHeaders = true)
        : base(streamId)
    {
        HeaderBlockFragment = headerBlock;
        EndStream = endStream;
        EndHeaders = endHeaders;
    }

    public override int SerializedSize => FrameHeaderSize + HeaderBlockFragment.Length;

    public override int WriteTo(ref Span<byte> span)
    {
        var flags = Headers.None;
        if (EndStream)
        {
            flags |= Headers.EndStream;
        }

        if (EndHeaders)
        {
            flags |= Headers.EndHeaders;
        }

        WriteHeader(ref span, HeaderBlockFragment.Length, FrameType.Headers, (byte)flags, StreamId);
        span = span[FrameHeaderSize..];
        HeaderBlockFragment.Span.CopyTo(span);
        span = span[HeaderBlockFragment.Length..];
        return SerializedSize;
    }
}

public sealed class ContinuationFrame : Http2Frame
{
    public override FrameType Type => FrameType.Continuation;
    public ReadOnlyMemory<byte> HeaderBlockFragment { get; }
    public bool EndHeaders { get; }

    public ContinuationFrame(int streamId, ReadOnlyMemory<byte> headerBlock, bool endHeaders = true) : base(streamId)
    {
        HeaderBlockFragment = headerBlock;
        EndHeaders = endHeaders;
    }

    public override int SerializedSize => FrameHeaderSize + HeaderBlockFragment.Length;

    public override int WriteTo(ref Span<byte> span)
    {
        var flags = EndHeaders ? (byte)ContinuationFlags.EndHeaders : (byte)0;
        WriteHeader(ref span, HeaderBlockFragment.Length, FrameType.Continuation, flags, StreamId);
        span = span[FrameHeaderSize..];
        HeaderBlockFragment.Span.CopyTo(span);
        span = span[HeaderBlockFragment.Length..];
        return SerializedSize;
    }
}

public sealed class RstStreamFrame : Http2Frame
{
    public override FrameType Type => FrameType.RstStream;
    public Http2ErrorCode ErrorCode { get; }

    public RstStreamFrame(int streamId, Http2ErrorCode errorCode) : base(streamId)
        => ErrorCode = errorCode;

    public override int SerializedSize => FrameHeaderSize + 4;

    public override int WriteTo(ref Span<byte> span)
    {
        WriteHeader(ref span, 4, FrameType.RstStream, 0, StreamId);
        span = span[FrameHeaderSize..];
        BinaryPrimitives.WriteUInt32BigEndian(span, (uint)ErrorCode);
        span = span[4..];
        return SerializedSize;
    }
}

public sealed class SettingsFrame : Http2Frame
{
    public override FrameType Type => FrameType.Settings;
    public IReadOnlyList<(SettingsParameter, uint)> Parameters { get; }
    public bool IsAck { get; }

    public SettingsFrame(IReadOnlyList<(SettingsParameter Key, uint Value)> parameters, bool isAck = false) : base(0)
    {
        Parameters = parameters;
        IsAck = isAck;
    }

    public override int SerializedSize => FrameHeaderSize + (IsAck ? 0 : Parameters.Count * 6);

    public override int WriteTo(ref Span<byte> span)
    {
        var payloadSize = IsAck ? 0 : Parameters.Count * 6;
        var flags = IsAck ? (byte)Settings.Ack : (byte)0;
        WriteHeader(ref span, payloadSize, FrameType.Settings, flags, 0);
        span = span[FrameHeaderSize..];

        foreach (var (key, val) in Parameters)
        {
            BinaryPrimitives.WriteUInt16BigEndian(span, (ushort)key);
            BinaryPrimitives.WriteUInt32BigEndian(span[2..], val);
            span = span[6..];
        }

        return SerializedSize;
    }

    public static byte[] SettingsAck()
    {
        var buf = new byte[FrameHeaderSize];
        var span = buf.AsSpan();
        WriteHeader(ref span, 0, FrameType.Settings, (byte)Settings.Ack, 0);
        return buf;
    }
}

public sealed class PingFrame : Http2Frame
{
    public override FrameType Type => FrameType.Ping;
    public byte[] Data { get; }
    public bool IsAck { get; }

    public PingFrame(byte[] data, bool isAck = false) : base(0)
    {
        if (data.Length != 8)
        {
            throw new ArgumentException("PING data must be exactly 8 bytes.", nameof(data));
        }

        Data = data;
        IsAck = isAck;
    }

    public override int SerializedSize => FrameHeaderSize + 8;

    public override int WriteTo(ref Span<byte> span)
    {
        var flags = IsAck ? (byte)PingFlags.Ack : (byte)0;
        WriteHeader(ref span, 8, FrameType.Ping, flags, 0);
        span = span[FrameHeaderSize..];
        Data.CopyTo(span);
        span = span[8..];
        return SerializedSize;
    }
}

public sealed class GoAwayFrame : Http2Frame
{
    public override FrameType Type => FrameType.GoAway;
    public int LastStreamId { get; }
    public Http2ErrorCode ErrorCode { get; }
    public byte[] DebugData { get; }

    public GoAwayFrame(int lastStreamId, Http2ErrorCode errorCode, byte[]? debugData = null) : base(0)
    {
        if (lastStreamId < 0)
        {
            throw new Http2Exception("Invalid LastStreamId");
        }

        LastStreamId = lastStreamId;
        ErrorCode = errorCode;
        DebugData = debugData ?? [];
    }

    public override int SerializedSize => FrameHeaderSize + 8 + DebugData.Length;

    public override int WriteTo(ref Span<byte> span)
    {
        var payloadSize = 8 + DebugData.Length;
        WriteHeader(ref span, payloadSize, FrameType.GoAway, 0, 0);
        span = span[FrameHeaderSize..];
        BinaryPrimitives.WriteUInt32BigEndian(span, (uint)LastStreamId & 0x7FFFFFFFu);
        BinaryPrimitives.WriteUInt32BigEndian(span[4..], (uint)ErrorCode);
        span = span[8..];
        DebugData.CopyTo(span);
        span = span[DebugData.Length..];
        return SerializedSize;
    }
}

public sealed class WindowUpdateFrame : Http2Frame
{
    public override FrameType Type => FrameType.WindowUpdate;
    public int Increment { get; }

    public WindowUpdateFrame(int streamId, int increment) : base(streamId)
    {
        if (increment is < 1 or > 0x7FFFFFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(increment));
        }

        Increment = increment;
    }

    public override int SerializedSize => FrameHeaderSize + 4;

    public override int WriteTo(ref Span<byte> span)
    {
        WriteHeader(ref span, 4, FrameType.WindowUpdate, 0, StreamId);
        span = span[FrameHeaderSize..];
        BinaryPrimitives.WriteUInt32BigEndian(span, (uint)Increment & 0x7FFFFFFFu);
        span = span[4..];
        return SerializedSize;
    }
}

/// <summary>
/// Represents an HTTP/2 frame with an unrecognized type.
/// RFC 7540 §4.1 / RFC 9113 §5.5: Unknown frame types MUST be ignored.
/// Preserved in the decoded output so callers can inspect or discard as needed.
/// </summary>
public sealed class UnknownFrame : Http2Frame
{
    public byte RawType { get; }
    public override FrameType Type => (FrameType)RawType;
    public byte[] Payload { get; }

    public UnknownFrame(byte rawType, int streamId, byte[] payload) : base(streamId)
    {
        RawType = rawType;
        Payload = payload;
    }

    public override int SerializedSize => FrameHeaderSize + Payload.Length;

    public override int WriteTo(ref Span<byte> span)
    {
        WriteHeader(ref span, Payload.Length, Type, 0, StreamId);
        span = span[FrameHeaderSize..];
        Payload.CopyTo(span);
        span = span[Payload.Length..];
        return SerializedSize;
    }
}

public sealed class PushPromiseFrame : Http2Frame
{
    public override FrameType Type => FrameType.PushPromise;
    public int PromisedStreamId { get; }
    public ReadOnlyMemory<byte> HeaderBlockFragment { get; }
    public bool EndHeaders { get; }

    public PushPromiseFrame(int streamId, int promisedStreamId, ReadOnlyMemory<byte> headerBlock,
        bool endHeaders = true)
        : base(streamId)
    {
        PromisedStreamId = promisedStreamId;
        HeaderBlockFragment = headerBlock;
        EndHeaders = endHeaders;
    }

    public override int SerializedSize => FrameHeaderSize + 4 + HeaderBlockFragment.Length;

    public override int WriteTo(ref Span<byte> span)
    {
        var payloadSize = 4 + HeaderBlockFragment.Length;
        var flags = EndHeaders ? (byte)Headers.EndHeaders : (byte)0;
        WriteHeader(ref span, payloadSize, FrameType.PushPromise, flags, StreamId);
        span = span[FrameHeaderSize..];
        BinaryPrimitives.WriteUInt32BigEndian(span, (uint)PromisedStreamId & 0x7FFFFFFFu);
        span = span[4..];
        HeaderBlockFragment.Span.CopyTo(span);
        span = span[HeaderBlockFragment.Length..];
        return SerializedSize;
    }
}