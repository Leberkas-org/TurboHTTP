using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace TurboHttp.Protocol;

/// <summary>
/// Decodes raw bytes into HTTP/2 frame objects.
/// Handles TCP fragmentation via an internal remainder buffer.
/// Pure frame parsing — no HPACK, no stream state.
/// </summary>
public sealed class Http2FrameDecoder
{
    private ReadOnlyMemory<byte> _remainder = ReadOnlyMemory<byte>.Empty;

    /// <summary>
    /// Feeds bytes and returns all complete frames decoded so far.
    /// Incomplete trailing bytes are buffered for the next call.
    /// </summary>
    public IReadOnlyList<Http2Frame> Decode(ReadOnlyMemory<byte> incoming)
    {
        var working = _remainder.IsEmpty ? incoming : Combine(_remainder, incoming);
        var frames = new List<Http2Frame>();

        while (working.Length >= 9)
        {
            var span = working.Span;
            var payloadLen = (span[0] << 16) | (span[1] << 8) | span[2];

            if (working.Length < 9 + payloadLen)
            {
                break;
            }

            var type = (FrameType)span[3];
            var flags = span[4];
            var streamId = (int)(BinaryPrimitives.ReadUInt32BigEndian(span[5..]) & 0x7FFFFFFFu);
            var payload = working.Slice(9, payloadLen);

            var frame = CreateFrame(type, flags, streamId, payload);
            // RFC 7540 §4.1 / RFC 9113 §5.5: Unknown frame types MUST be ignored.
            if (frame != null)
            {
                frames.Add(frame);
            }
            working = working[(9 + payloadLen)..];
        }

        _remainder = working.IsEmpty ? ReadOnlyMemory<byte>.Empty : working.ToArray();
        return frames;
    }

    public void Reset() => _remainder = ReadOnlyMemory<byte>.Empty;

    // ── Frame object creation — all 9 types ──────────────────────────────────

    private static Http2Frame? CreateFrame(FrameType type, byte flags, int streamId, ReadOnlyMemory<byte> payload)
    {
        return type switch
        {
            FrameType.Data => new DataFrame(
                streamId,
                payload.ToArray(),
                (flags & (byte)DataFlags.EndStream) != 0),

            FrameType.Headers => ParseHeadersFrame(flags, streamId, payload),

            FrameType.Continuation => new ContinuationFrame(
                streamId,
                payload.ToArray(),
                (flags & (byte)ContinuationFlags.EndHeaders) != 0),

            FrameType.Ping => streamId != 0
                ? throw new Http2Exception(
                    "RFC 7540 §6.7: PING frame MUST be sent on stream 0.",
                    Http2ErrorCode.ProtocolError)
                : CreatePing(flags, payload),

            FrameType.Settings => streamId != 0
                ? throw new Http2Exception(
                    "RFC 7540 §6.5: SETTINGS frame MUST be sent on stream 0.",
                    Http2ErrorCode.ProtocolError)
                : ParseSettings(payload, flags),

            FrameType.WindowUpdate => CreateWindowUpdateFrame(streamId, payload),

            FrameType.RstStream => payload.Length == 4
                ? new RstStreamFrame(streamId, (Http2ErrorCode)BinaryPrimitives.ReadUInt32BigEndian(payload.Span))
                : throw new Http2Exception(
                    $"RFC 7540 §6.4: RST_STREAM frame must be exactly 4 bytes; got {payload.Length}.",
                    Http2ErrorCode.FrameSizeError),

            FrameType.GoAway => streamId != 0
                ? throw new Http2Exception(
                    "RFC 7540 §6.8: GOAWAY frame MUST be sent on stream 0.",
                    Http2ErrorCode.ProtocolError)
                : ParseGoAway(payload),

            FrameType.PushPromise => ParsePushPromise(streamId, flags, payload),

            // RFC 7540 §4.1 / RFC 9113 §5.5: Unknown frame types MUST be ignored.
            _ => new UnknownFrame((byte)type, streamId, payload.ToArray())
        };
    }

    private static HeadersFrame ParseHeadersFrame(byte flags, int streamId, ReadOnlyMemory<byte> payload)
    {
        var endStream = (flags & (byte)Headers.EndStream) != 0;
        var endHeaders = (flags & (byte)Headers.EndHeaders) != 0;
        var data = payload;

        if ((flags & 0x08) != 0) // PADDED
        {
            if (data.IsEmpty)
            {
                throw new Http2Exception("HEADERS PADDED frame: payload is empty",
                    Http2ErrorCode.ProtocolError);
            }

            var padLen = data.Span[0];
            if (1 + padLen > data.Length)
            {
                throw new Http2Exception("HEADERS PADDED frame: pad_length exceeds payload size",
                    Http2ErrorCode.ProtocolError);
            }

            data = data.Slice(1, data.Length - 1 - padLen);
        }

        if ((flags & 0x20) != 0) // PRIORITY — consume 4-byte stream dep + 1-byte weight
        {
            data = data.Length >= 5 ? data[5..] : ReadOnlyMemory<byte>.Empty;
        }

        return new HeadersFrame(streamId, data, endStream, endHeaders);
    }

    private static PingFrame CreatePing(byte flags, ReadOnlyMemory<byte> payload)
    {
        if (payload.Length != 8)
        {
            throw new Http2Exception($"PING frame must be exactly 8 bytes, got {payload.Length}",
                Http2ErrorCode.FrameSizeError);
        }

        return new PingFrame(payload.ToArray(), (flags & (byte)PingFlags.Ack) != 0);
    }

    private static SettingsFrame ParseSettings(ReadOnlyMemory<byte> payload, byte flags)
    {
        var isAck = (flags & (byte)Settings.Ack) != 0;

        // RFC 7540 §6.5: A SETTINGS frame with ACK flag MUST have an empty payload.
        if (isAck && payload.Length > 0)
        {
            throw new Http2Exception(
                "RFC 7540 §6.5: SETTINGS frame with ACK flag MUST have empty payload.",
                Http2ErrorCode.FrameSizeError);
        }

        // RFC 7540 §6.5: A SETTINGS payload length not a multiple of 6 octets is a FRAME_SIZE_ERROR.
        if (!isAck && payload.Length % 6 != 0)
        {
            throw new Http2Exception(
                $"RFC 7540 §6.5: SETTINGS payload length {payload.Length} is not a multiple of 6.",
                Http2ErrorCode.FrameSizeError);
        }

        var list = new List<(SettingsParameter, uint)>();
        var span = payload.Span;

        for (var i = 0; i + 6 <= span.Length; i += 6)
        {
            var key = (SettingsParameter)BinaryPrimitives.ReadUInt16BigEndian(span[i..]);
            var value = BinaryPrimitives.ReadUInt32BigEndian(span[(i + 2)..]);

            if (key == SettingsParameter.MaxFrameSize && (value < 16384 || value > 16777215))
            {
                throw new Http2Exception(
                    $"RFC 7540 §6.5.2: SETTINGS_MAX_FRAME_SIZE {value} is outside the valid range [16384, 16777215].",
                    Http2ErrorCode.ProtocolError);
            }

            list.Add((key, value));
        }

        return new SettingsFrame(list, isAck);
    }

    private static GoAwayFrame ParseGoAway(ReadOnlyMemory<byte> payload)
    {
        var span = payload.Span;
        var lastStream = (int)(BinaryPrimitives.ReadUInt32BigEndian(span) & 0x7FFFFFFFu);
        var errorCode = (Http2ErrorCode)BinaryPrimitives.ReadUInt32BigEndian(span[4..]);
        var debugData = span.Length > 8 ? payload[8..].ToArray() : [];
        return new GoAwayFrame(lastStream, errorCode, debugData);
    }

    private static PushPromiseFrame ParsePushPromise(
        int streamId, byte flags, ReadOnlyMemory<byte> payload)
    {
        var span = payload.Span;
        var promised = (int)(BinaryPrimitives.ReadUInt32BigEndian(span) & 0x7FFFFFFFu);
        var endHeaders = (flags & (byte)Headers.EndHeaders) != 0;
        var headerBlock = payload[4..].ToArray();
        return new PushPromiseFrame(streamId, promised, headerBlock, endHeaders);
    }

    private static WindowUpdateFrame CreateWindowUpdateFrame(int streamId, ReadOnlyMemory<byte> payload)
    {
        if (payload.Length != 4)
        {
            throw new Http2Exception(
                $"RFC 7540 §6.9: WINDOW_UPDATE payload must be exactly 4 bytes; got {payload.Length}.",
                Http2ErrorCode.FrameSizeError);
        }

        var increment = (int)(BinaryPrimitives.ReadUInt32BigEndian(payload.Span) & 0x7FFFFFFFu);
        if (increment == 0)
        {
            throw new Http2Exception(
                "RFC 7540 §6.9: WINDOW_UPDATE increment of 0 is a PROTOCOL_ERROR.",
                Http2ErrorCode.ProtocolError);
        }

        return new WindowUpdateFrame(streamId, increment);
    }

    private static ReadOnlyMemory<byte> Combine(ReadOnlyMemory<byte> a, ReadOnlyMemory<byte> b)
    {
        var result = new byte[a.Length + b.Length];
        a.Span.CopyTo(result);
        b.Span.CopyTo(result.AsSpan(a.Length));
        return result;
    }
}