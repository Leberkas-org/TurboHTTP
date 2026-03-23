using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace TurboHttp.Protocol.RFC9113;

/// <summary>
/// Decodes raw bytes into HTTP/2 frame objects.
/// Handles TCP fragmentation via an internal remainder buffer.
/// Pure frame parsing — no HPACK, no stream state.
/// </summary>
public sealed class Http2FrameDecoder
{
    // RFC 9113 §4.1: all frames begin with a fixed 9-octet header.
    private const int FrameHeaderSize = 9;

    // RFC 9113 §4.1: the reserved R bit must be ignored; mask it out of the stream identifier.
    private const uint StreamIdMask = 0x7FFFFFFFu;

    // RFC 9113 §6.3 / §6.4: PRIORITY and RST_STREAM payloads are exactly 4 bytes.
    private const int PriorityFieldSize = 5;  // stream dependency (4) + weight (1)
    private const int RstStreamPayloadSize = 4;

    // RFC 9113 §6.5: each SETTINGS parameter is a 6-byte identifier+value pair.
    private const int SettingsEntrySize = 6;
    private const int SettingsValueOffset = 2;  // value is at bytes [2..6) within the entry

    // RFC 9113 §6.5.2: SETTINGS_MAX_FRAME_SIZE must be in [2^14, 2^24−1].
    private const uint MinMaxFrameSize = 16_384;
    private const uint MaxMaxFrameSize = 16_777_215;

    // RFC 9113 §6.7: PING payload is exactly 8 bytes.
    private const int PingPayloadSize = 8;

    // RFC 9113 §6.8: GOAWAY has a fixed 8-byte header (last-stream-id + error-code).
    private const int GoAwayMinPayloadSize = 8;
    private const int GoAwayErrorCodeOffset = 4;

    // RFC 9113 §6.6: PUSH_PROMISE promised stream ID is 4 bytes; header block follows.
    private const int PushPromiseHeaderBlockOffset = 4;

    // RFC 9113 §6.9: WINDOW_UPDATE payload is exactly 4 bytes.
    private const int WindowUpdatePayloadSize = 4;

    // RFC 9113 §6.1 / §6.2: one-byte Pad Length field precedes padded data.
    private const int PadLengthFieldSize = 1;

    private ReadOnlyMemory<byte> _remainder = ReadOnlyMemory<byte>.Empty;

    // RFC 9113 §6.10: tracks whether we are awaiting a CONTINUATION frame.
    // When non-zero, only CONTINUATION on this stream ID is allowed.
    private int _awaitingContinuationStreamId;

    /// <summary>
    /// Feeds bytes and returns all complete frames decoded so far.
    /// Incomplete trailing bytes are buffered for the next call.
    /// </summary>
    public IReadOnlyList<Http2Frame> Decode(ReadOnlyMemory<byte> incoming)
    {
        var working = _remainder.IsEmpty ? incoming : Combine(_remainder, incoming);
        var frames = new List<Http2Frame>();

        while (working.Length >= FrameHeaderSize)
        {
            var span = working.Span;
            var payloadLen = (span[0] << 16) | (span[1] << 8) | span[2];

            if (working.Length < FrameHeaderSize + payloadLen)
            {
                break;
            }

            var type = (FrameType)span[3];
            var flags = span[4];
            var streamId = (int)(BinaryPrimitives.ReadUInt32BigEndian(span[5..]) & StreamIdMask);
            var payload = working.Slice(FrameHeaderSize, payloadLen);

            var frame = CreateFrame(type, flags, streamId, payload);
            // RFC 9113 §5.5: Unknown frame types MUST be ignored.
            if (frame != null)
            {
                ValidateContinuationState(type, streamId);
                UpdateContinuationState(frame);
                frames.Add(frame);
            }

            working = working[(FrameHeaderSize + payloadLen)..];
        }

        _remainder = working.IsEmpty ? ReadOnlyMemory<byte>.Empty : working.ToArray();
        return frames;
    }

    public void Reset()
    {
        _remainder = ReadOnlyMemory<byte>.Empty;
        _awaitingContinuationStreamId = 0;
    }

    private static Http2Frame? CreateFrame(FrameType type, byte flags, int streamId, ReadOnlyMemory<byte> payload)
    {
        return type switch
        {
            FrameType.Data => ParseDataFrame(flags, streamId, payload),

            FrameType.Headers => ParseHeadersFrame(flags, streamId, payload),

            FrameType.Continuation => streamId == 0
                ? throw new Http2Exception(
                    "RFC 9113 §6.10: CONTINUATION frame MUST be associated with a stream; stream 0 is invalid.",
                    Http2ErrorCode.ProtocolError)
                : new ContinuationFrame(
                    streamId,
                    payload,
                    (flags & (byte)ContinuationFlags.EndHeaders) != 0),

            FrameType.Ping => streamId != 0
                ? throw new Http2Exception("RFC 9113 §6.7: PING frame MUST be sent on stream 0.")
                : CreatePing(flags, payload),

            FrameType.Settings => streamId != 0
                ? throw new Http2Exception("RFC 9113 §6.5: SETTINGS frame MUST be sent on stream 0.")
                : ParseSettings(payload, flags),

            FrameType.WindowUpdate => CreateWindowUpdateFrame(streamId, payload),

            FrameType.RstStream => payload.Length == RstStreamPayloadSize
                ? new RstStreamFrame(streamId, (Http2ErrorCode)BinaryPrimitives.ReadUInt32BigEndian(payload.Span))
                : throw new Http2Exception(
                    $"RFC 9113 §6.4: RST_STREAM frame must be exactly {RstStreamPayloadSize} bytes; got {payload.Length}.",
                    Http2ErrorCode.FrameSizeError),

            FrameType.GoAway => streamId != 0
                ? throw new Http2Exception(
                    "RFC 9113 §6.8: GOAWAY frame MUST be sent on stream 0.")
                : ParseGoAway(payload),

            FrameType.PushPromise => ParsePushPromise(streamId, flags, payload),

            // RFC 9113 §5.5: Unknown frame types MUST be ignored.
            _ => null
        };
    }

    private static DataFrame ParseDataFrame(byte flags, int streamId, ReadOnlyMemory<byte> payload)
    {
        if (streamId == 0)
        {
            throw new Http2Exception(
                "RFC 9113 §6.1: DATA frame MUST be associated with a stream; stream 0 is invalid.",
                Http2ErrorCode.ProtocolError);
        }

        var endStream = (flags & (byte)DataFlags.EndStream) != 0;
        var data = payload;

        if ((flags & (byte)DataFlags.Padded) != 0)
        {
            if (data.IsEmpty)
            {
                throw new Http2Exception("DATA PADDED frame: payload is empty",
                    Http2ErrorCode.ProtocolError);
            }

            var padLen = data.Span[0];
            if (PadLengthFieldSize + padLen > data.Length)
            {
                throw new Http2Exception("DATA PADDED frame: pad_length exceeds payload size",
                    Http2ErrorCode.ProtocolError);
            }

            data = data.Slice(PadLengthFieldSize, data.Length - PadLengthFieldSize - padLen);
        }

        return new DataFrame(streamId, data, endStream);
    }

    private static HeadersFrame ParseHeadersFrame(byte flags, int streamId, ReadOnlyMemory<byte> payload)
    {
        var endStream = (flags & (byte)Headers.EndStream) != 0;
        var endHeaders = (flags & (byte)Headers.EndHeaders) != 0;
        var data = payload;

        if ((flags & (byte)Headers.Padded) != 0)
        {
            if (data.IsEmpty)
            {
                throw new Http2Exception("HEADERS PADDED frame: payload is empty",
                    Http2ErrorCode.ProtocolError);
            }

            var padLen = data.Span[0];
            if (PadLengthFieldSize + padLen > data.Length)
            {
                throw new Http2Exception("HEADERS PADDED frame: pad_length exceeds payload size",
                    Http2ErrorCode.ProtocolError);
            }

            data = data.Slice(PadLengthFieldSize, data.Length - PadLengthFieldSize - padLen);
        }

        if ((flags & (byte)Headers.Priority) != 0) // PRIORITY — consume 4-byte stream dep + 1-byte weight
        {
            data = data.Length >= PriorityFieldSize ? data[PriorityFieldSize..] : ReadOnlyMemory<byte>.Empty;
        }

        return new HeadersFrame(streamId, data, endStream, endHeaders);
    }

    private static PingFrame CreatePing(byte flags, ReadOnlyMemory<byte> payload)
    {
        if (payload.Length != PingPayloadSize)
        {
            throw new Http2Exception($"PING frame must be exactly {PingPayloadSize} bytes, got {payload.Length}",
                Http2ErrorCode.FrameSizeError);
        }

        return new PingFrame(payload, (flags & (byte)PingFlags.Ack) != 0);
    }

    private static SettingsFrame ParseSettings(ReadOnlyMemory<byte> payload, byte flags)
    {
        var isAck = (flags & (byte)Settings.Ack) != 0;

        // RFC 9113 §6.5: A SETTINGS frame with ACK flag MUST have an empty payload.
        if (isAck && payload.Length > 0)
        {
            throw new Http2Exception(
                "RFC 9113 §6.5: SETTINGS frame with ACK flag MUST have empty payload.",
                Http2ErrorCode.FrameSizeError);
        }

        // RFC 9113 §6.5: A SETTINGS payload length not a multiple of 6 octets is a FRAME_SIZE_ERROR.
        if (!isAck && payload.Length % SettingsEntrySize != 0)
        {
            throw new Http2Exception(
                $"RFC 9113 §6.5: SETTINGS payload length {payload.Length} is not a multiple of {SettingsEntrySize}.",
                Http2ErrorCode.FrameSizeError);
        }

        var list = new List<(SettingsParameter, uint)>();
        var span = payload.Span;

        for (var i = 0; i + SettingsEntrySize <= span.Length; i += SettingsEntrySize)
        {
            var key = (SettingsParameter)BinaryPrimitives.ReadUInt16BigEndian(span[i..]);
            var value = BinaryPrimitives.ReadUInt32BigEndian(span[(i + SettingsValueOffset)..]);

            if (key == SettingsParameter.MaxFrameSize && (value < MinMaxFrameSize || value > MaxMaxFrameSize))
            {
                throw new Http2Exception(
                    $"RFC 9113 §6.5.2: SETTINGS_MAX_FRAME_SIZE {value} is outside the valid range [{MinMaxFrameSize}, {MaxMaxFrameSize}].");
            }

            list.Add((key, value));
        }

        return new SettingsFrame(list, isAck);
    }

    private static GoAwayFrame ParseGoAway(ReadOnlyMemory<byte> payload)
    {
        if (payload.Length < GoAwayMinPayloadSize)
        {
            throw new Http2Exception(
                $"RFC 9113 §6.8: GOAWAY payload must be at least {GoAwayMinPayloadSize} bytes; got {payload.Length}.",
                Http2ErrorCode.FrameSizeError);
        }

        var span = payload.Span;
        var lastStream = (int)(BinaryPrimitives.ReadUInt32BigEndian(span) & StreamIdMask);
        var errorCode = (Http2ErrorCode)BinaryPrimitives.ReadUInt32BigEndian(span[GoAwayErrorCodeOffset..]);
        var debugData = span.Length > GoAwayMinPayloadSize ? payload[GoAwayMinPayloadSize..] : ReadOnlyMemory<byte>.Empty;
        return new GoAwayFrame(lastStream, errorCode, debugData);
    }

    private static PushPromiseFrame ParsePushPromise(
        int streamId, byte flags, ReadOnlyMemory<byte> payload)
    {
        if (payload.Length < PushPromiseHeaderBlockOffset)
        {
            throw new Http2Exception(
                $"RFC 9113 §6.6: PUSH_PROMISE payload must be at least {PushPromiseHeaderBlockOffset} bytes; got {payload.Length}.",
                Http2ErrorCode.FrameSizeError);
        }

        var span = payload.Span;
        var promised = (int)(BinaryPrimitives.ReadUInt32BigEndian(span) & StreamIdMask);
        var endHeaders = (flags & (byte)Headers.EndHeaders) != 0;
        return new PushPromiseFrame(streamId, promised, payload[PushPromiseHeaderBlockOffset..], endHeaders);
    }

    private static WindowUpdateFrame CreateWindowUpdateFrame(int streamId, ReadOnlyMemory<byte> payload)
    {
        if (payload.Length != WindowUpdatePayloadSize)
        {
            throw new Http2Exception(
                $"RFC 9113 §6.9: WINDOW_UPDATE payload must be exactly {WindowUpdatePayloadSize} bytes; got {payload.Length}.",
                Http2ErrorCode.FrameSizeError);
        }

        var increment = (int)(BinaryPrimitives.ReadUInt32BigEndian(payload.Span) & StreamIdMask);
        if (increment == 0)
        {
            throw new Http2Exception(
                "RFC 9113 §6.9: WINDOW_UPDATE increment of 0 is a PROTOCOL_ERROR.");
        }

        return new WindowUpdateFrame(streamId, increment);
    }

    /// <summary>
    /// RFC 9113 §6.10: validates that CONTINUATION state constraints are met.
    /// When awaiting CONTINUATION, only CONTINUATION on the same stream is allowed.
    /// When not awaiting CONTINUATION, a bare CONTINUATION is invalid.
    /// </summary>
    private void ValidateContinuationState(FrameType type, int streamId)
    {
        if (_awaitingContinuationStreamId != 0)
        {
            if (type != FrameType.Continuation)
            {
                throw new Http2Exception(
                    $"RFC 9113 §6.10: Expected CONTINUATION frame on stream {_awaitingContinuationStreamId}, but received {type}.",
                    Http2ErrorCode.ProtocolError);
            }

            if (streamId != _awaitingContinuationStreamId)
            {
                throw new Http2Exception(
                    $"RFC 9113 §6.10: Expected CONTINUATION on stream {_awaitingContinuationStreamId}, but received on stream {streamId}.",
                    Http2ErrorCode.ProtocolError);
            }
        }
        else if (type == FrameType.Continuation)
        {
            throw new Http2Exception(
                "RFC 9113 §6.10: CONTINUATION frame received without preceding HEADERS or PUSH_PROMISE.",
                Http2ErrorCode.ProtocolError);
        }
    }

    /// <summary>
    /// Tracks whether the decoder is awaiting a CONTINUATION frame.
    /// HEADERS/PUSH_PROMISE without END_HEADERS sets the expectation;
    /// CONTINUATION with END_HEADERS clears it.
    /// </summary>
    private void UpdateContinuationState(Http2Frame frame)
    {
        switch (frame)
        {
            case HeadersFrame h when !h.EndHeaders:
                _awaitingContinuationStreamId = h.StreamId;
                break;
            case HeadersFrame:
                _awaitingContinuationStreamId = 0;
                break;
            case PushPromiseFrame pp when !pp.EndHeaders:
                _awaitingContinuationStreamId = pp.StreamId;
                break;
            case PushPromiseFrame:
                _awaitingContinuationStreamId = 0;
                break;
            case ContinuationFrame c when c.EndHeaders:
                _awaitingContinuationStreamId = 0;
                break;
        }
    }

    private static ReadOnlyMemory<byte> Combine(ReadOnlyMemory<byte> a, ReadOnlyMemory<byte> b)
    {
        var result = new byte[a.Length + b.Length];
        a.Span.CopyTo(result);
        b.Span.CopyTo(result.AsSpan(a.Length));
        return result;
    }
}