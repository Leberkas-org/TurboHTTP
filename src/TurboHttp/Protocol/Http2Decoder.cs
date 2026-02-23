using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Net;
using System.Net.Http;

namespace TurboHttp.Protocol;

public sealed class Http2Decoder
{
    private readonly HpackDecoder _hpack = new();
    private ReadOnlyMemory<byte> _remainder = ReadOnlyMemory<byte>.Empty;
    private readonly Dictionary<int, StreamState> _streams = new();
    private readonly HashSet<int> _closedStreamIds = [];
    private readonly HashSet<int> _promisedStreamIds = [];

    private int _continuationStreamId;
    private byte[]? _continuationBuffer;
    private int _continuationBufferLength;
    private bool _continuationEndStream;
    private int _continuationFrameCount;

    // Security counters (reset per connection via Reset()).
    private int _rstStreamCount;
    private int _emptyDataFrameCount;

    // RFC 7540 §6.5.2: Default MAX_FRAME_SIZE is 2^14 (16384).
    private int _maxFrameSize = 16384;

    // RFC 7540 §5.1.2 / §6.5.2: MAX_CONCURRENT_STREAMS limit and active count.
    private int _maxConcurrentStreams = int.MaxValue;
    private int _activeStreamCount = 0;

    // RFC 7540 §5.2: Connection-level receive window (how much DATA server may send us).
    private int _connectionReceiveWindow = 65535;

    // RFC 7540 §5.2: Connection-level send window (updated by incoming WINDOW_UPDATE).
    private long _connectionSendWindow = 65535;

    // Set to true after we receive a GOAWAY frame; blocks new stream creation.
    private bool _receivedGoAway;

    /// <summary>Returns the current number of active (open) streams.</summary>
    public int GetActiveStreamCount() => _activeStreamCount;

    /// <summary>Returns the MAX_CONCURRENT_STREAMS limit (default int.MaxValue).</summary>
    public int GetMaxConcurrentStreams() => _maxConcurrentStreams;

    /// <summary>Returns the current connection-level receive window.</summary>
    public int GetConnectionReceiveWindow() => _connectionReceiveWindow;

    /// <summary>Returns the current connection-level send window.</summary>
    public long GetConnectionSendWindow() => _connectionSendWindow;

    /// <summary>Returns the receive window for the given stream, or 65535 if the stream is unknown.</summary>
    public int GetStreamReceiveWindow(int streamId) =>
        _streams.TryGetValue(streamId, out var s) ? s.ReceiveWindow : 65535;

    /// <summary>
    /// For testing: sets the connection-level receive window so tests can trigger FLOW_CONTROL_ERROR
    /// without needing to transmit gigabytes of data.
    /// </summary>
    public void SetConnectionReceiveWindow(int value) => _connectionReceiveWindow = value;

    /// <summary>
    /// Sets the stream-level receive window after sending a stream WINDOW_UPDATE,
    /// so the decoder will accept future DATA frames within the new window.
    /// </summary>
    public void SetStreamReceiveWindow(int streamId, int value)
    {
        if (_streams.TryGetValue(streamId, out var state))
        {
            state.SetReceiveWindow(value);
        }
    }

    /// <summary>
    /// RFC 7540 §3.5 — Validates that bytes from the server begin with a SETTINGS frame
    /// (the mandatory server connection preface).
    /// Returns false if bytes are incomplete (caller should buffer and retry).
    /// Throws Http2Exception(PROTOCOL_ERROR) if the bytes contain a wrong frame type.
    /// </summary>
    public bool ValidateServerPreface(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length < 9)
        {
            return false; // incomplete frame header — need more bytes
        }

        var span = bytes.Span;
        var frameType = (FrameType)span[3];
        var streamId = (int)(BinaryPrimitives.ReadUInt32BigEndian(span[5..]) & 0x7FFFFFFFu);

        if (frameType != FrameType.Settings || streamId != 0)
        {
            throw new Http2Exception(
                $"RFC 7540 §3.5: Server connection preface must be a SETTINGS frame on stream 0; got type={frameType}, streamId={streamId}.",
                Http2ErrorCode.ProtocolError);
        }

        return true;
    }

    public bool TryDecode(in ReadOnlyMemory<byte> incoming, out Http2DecodeResult result)
    {
        result = Http2DecodeResult.Empty;
        var responses = ImmutableList.CreateBuilder<(int StreamId, HttpResponseMessage Response)>();
        var controlFrames = ImmutableList.CreateBuilder<Http2Frame>();
        var settingsList = ImmutableList.CreateBuilder<IReadOnlyList<(SettingsParameter, uint)>>();
        var pingAcks = ImmutableList.CreateBuilder<byte[]>();
        var windowUpdates = ImmutableList.CreateBuilder<(int StreamId, int Increment)>();
        var rstStreams = ImmutableList.CreateBuilder<(int StreamId, Http2ErrorCode Error)>();
        var settingsAcksToSend = ImmutableList.CreateBuilder<byte[]>();
        var pingAcksToSend = ImmutableList.CreateBuilder<byte[]>();
        var promisedStreamIds = ImmutableList.CreateBuilder<int>();
        GoAwayFrame? goAway = null;

        var working = Combine(_remainder, incoming);
        _remainder = ReadOnlyMemory<byte>.Empty;

        if (working.Length < 9)
        {
            _remainder = working;
            return false;
        }

        var decoded = false;

        while (working.Length >= 9)
        {
            var span = working.Span;
            var payloadLength = (span[0] << 16) | (span[1] << 8) | span[2];

            if (working.Length < 9 + payloadLength)
            {
                _remainder = working;
                break;
            }

            var frameType = (FrameType)span[3];
            var flags = span[4];

            // RFC 7540 §4.1: The R-bit MUST remain unset when sending.
            // Treat a set R-bit as a connection error (PROTOCOL_ERROR).
            var rawStreamWord = BinaryPrimitives.ReadUInt32BigEndian(span[5..]);
            if ((rawStreamWord & 0x80000000u) != 0)
            {
                throw new Http2Exception(
                    "RFC 7540 §4.1: R-bit MUST be unset; a set R-bit is a PROTOCOL_ERROR.",
                    Http2ErrorCode.ProtocolError);
            }

            var streamId = (int)(rawStreamWord & 0x7FFFFFFFu);

            // RFC 7540 §4.3: A frame size that exceeds MAX_FRAME_SIZE is a FRAME_SIZE_ERROR.
            if (payloadLength > _maxFrameSize)
            {
                throw new Http2Exception(
                    $"RFC 7540 §4.3: Frame payload {payloadLength} exceeds MAX_FRAME_SIZE {_maxFrameSize}.",
                    Http2ErrorCode.FrameSizeError);
            }

            // RFC 7540 §6.10: After a HEADERS without END_HEADERS, only CONTINUATION is allowed.
            if (_continuationBuffer != null && frameType != FrameType.Continuation)
            {
                throw new Http2Exception(
                    $"RFC 7540 §6.10: Expected CONTINUATION frame but received {frameType} while awaiting header block completion.",
                    Http2ErrorCode.ProtocolError);
            }

            var payload = working.Slice(9, payloadLength);
            working = working[(9 + payloadLength)..];
            decoded = true;

            switch (frameType)
            {
                case FrameType.Data:
                    HandleData(payload, flags, streamId, responses);
                    break;

                case FrameType.Headers:
                    HandleHeaders(payload, flags, streamId, responses);
                    break;

                case FrameType.Continuation:
                    HandleContinuation(payload, flags, streamId, responses);
                    break;

                case FrameType.Settings:
                    HandleSettings(flags, payload, payloadLength, settingsList, controlFrames, settingsAcksToSend);
                    break;

                case FrameType.Ping:
                    HandlePing(flags, payload, controlFrames, pingAcks, pingAcksToSend);
                    break;

                case FrameType.WindowUpdate:
                    HandleWindowUpdate(payload, streamId, windowUpdates);
                    break;

                case FrameType.RstStream:
                    if (payload.Length >= 4)
                    {
                        var error = (Http2ErrorCode)BinaryPrimitives.ReadUInt32BigEndian(payload.Span);
                        rstStreams.Add((streamId, error));

                        // Decrement active count only if the stream was being tracked.
                        if (_streams.Remove(streamId))
                        {
                            _activeStreamCount--;
                        }

                        _closedStreamIds.Add(streamId);

                        // Security: rapid RST_STREAM cycling protection (mitigates CVE-2023-44487).
                        _rstStreamCount++;
                        if (_rstStreamCount > 100)
                        {
                            throw new Http2Exception(
                                $"RFC 7540 security: Excessive RST_STREAM frames ({_rstStreamCount}) — possible rapid-reset attack (CVE-2023-44487).",
                                Http2ErrorCode.ProtocolError);
                        }
                    }

                    break;

                case FrameType.GoAway:
                    goAway = ParseGoAway(payload);
                    _receivedGoAway = true;
                    break;

                case FrameType.PushPromise:
                    HandlePushPromise(payload, flags, streamId, promisedStreamIds);
                    break;

                case FrameType.Priority:
                    break;

                default:
                    // RFC 7540 §4.1: Unknown frame types are ignored.
                    break;
            }
        }

        if (!decoded) return false;

        result = new Http2DecodeResult(
            responses.ToImmutable(),
            controlFrames.ToImmutable(),
            settingsList.ToImmutable(),
            pingAcks.ToImmutable(),
            windowUpdates.ToImmutable(),
            rstStreams.ToImmutable(),
            goAway,
            settingsAcksToSend.ToImmutable(),
            pingAcksToSend.ToImmutable(),
            promisedStreamIds.ToImmutable());
        return true;
    }

    public void Reset()
    {
        _remainder = ReadOnlyMemory<byte>.Empty;
        _streams.Clear();
        _closedStreamIds.Clear();
        _promisedStreamIds.Clear();
        _continuationStreamId = 0;
        _continuationBuffer = null;
        _continuationBufferLength = 0;
        _continuationFrameCount = 0;
        _continuationEndStream = false;
        _receivedGoAway = false;
        _maxFrameSize = 16384;
        _connectionReceiveWindow = 65535;
        _connectionSendWindow = 65535;
        _rstStreamCount = 0;
        _emptyDataFrameCount = 0;
        _maxConcurrentStreams = int.MaxValue;
        _activeStreamCount = 0;
    }

    // ========================================================================
    // FRAME HANDLERS
    // ========================================================================
    private void HandleData(
        ReadOnlyMemory<byte> payload,
        byte flags,
        int streamId,
        ImmutableList<(int, HttpResponseMessage)>.Builder responses)
    {
        // RFC 7540 §6.1: DATA frames MUST be associated with a stream.
        if (streamId == 0)
        {
            throw new Http2Exception(
                "RFC 7540 §6.1: DATA frame received on stream 0.",
                Http2ErrorCode.ProtocolError);
        }

        // RFC 7540 §6.1 / §5.1: DATA on a closed stream is a STREAM_CLOSED error.
        if (_closedStreamIds.Contains(streamId))
        {
            throw new Http2Exception(
                $"RFC 7540 §6.1: DATA received on closed stream {streamId}.",
                Http2ErrorCode.StreamClosed);
        }

        var data = StripPadding(payload, flags, padded: (flags & 0x8) != 0);

        // Security: reject excessive zero-length DATA frames (slow-loris / amplification protection).
        if (data.Length == 0)
        {
            _emptyDataFrameCount++;
            if (_emptyDataFrameCount > 10000)
            {
                throw new Http2Exception(
                    $"RFC 7540 security: Excessive zero-length DATA frames ({_emptyDataFrameCount}) — connection terminated.",
                    Http2ErrorCode.ProtocolError);
            }
        }

        // RFC 7540 §5.2: Enforce connection-level receive window.
        if (data.Length > _connectionReceiveWindow)
        {
            throw new Http2Exception(
                $"RFC 7540 §5.2: Peer sent {data.Length} bytes but connection receive window is {_connectionReceiveWindow}.",
                Http2ErrorCode.FlowControlError);
        }

        if (!_streams.TryGetValue(streamId, out var state))
        {
            return;
        }

        // RFC 7540 §5.2: Enforce stream-level receive window.
        if (data.Length > state.ReceiveWindow)
        {
            throw new Http2Exception(
                $"RFC 7540 §5.2: Peer sent {data.Length} bytes but stream {streamId} receive window is {state.ReceiveWindow}.",
                Http2ErrorCode.FlowControlError);
        }

        // Deduct from receive windows.
        _connectionReceiveWindow -= data.Length;
        state.DeductReceiveWindow(data.Length);

        state.AppendBody(data.Span);

        if ((flags & (byte)DataFlags.EndStream) != 0)
        {
            var response = state.BuildResponse();
            _streams.Remove(streamId);
            _closedStreamIds.Add(streamId);
            responses.Add((streamId, response));
            _activeStreamCount--;
        }
    }

    private void HandleHeaders(
        ReadOnlyMemory<byte> payload,
        byte flags,
        int streamId,
        ImmutableList<(int, HttpResponseMessage)>.Builder responses)
    {
        // RFC 7540 §6.2: HEADERS frames MUST be associated with a stream.
        if (streamId == 0)
        {
            throw new Http2Exception(
                "RFC 7540 §6.2: HEADERS frame received on stream 0.",
                Http2ErrorCode.ProtocolError);
        }

        // RFC 7540 §5.1: Reusing a previously closed stream ID is PROTOCOL_ERROR.
        if (_closedStreamIds.Contains(streamId))
        {
            throw new Http2Exception(
                $"RFC 7540 §5.1: HEADERS received on closed stream {streamId}; reusing a closed stream ID is PROTOCOL_ERROR.",
                Http2ErrorCode.ProtocolError);
        }

        // RFC 7540 §5.1.1: Server-initiated (even) stream IDs must be pre-announced via PUSH_PROMISE.
        if (streamId % 2 == 0 && !_promisedStreamIds.Contains(streamId))
        {
            throw new Http2Exception(
                $"RFC 7540 §5.1.1: HEADERS on even stream {streamId} without preceding PUSH_PROMISE is PROTOCOL_ERROR.",
                Http2ErrorCode.ProtocolError);
        }

        // RFC 7540: No new streams accepted after GOAWAY.
        if (_receivedGoAway && !_streams.ContainsKey(streamId))
        {
            throw new Http2Exception(
                $"RFC 7540 §6.8: No new streams accepted after GOAWAY; stream {streamId} rejected.",
                Http2ErrorCode.ProtocolError);
        }

        // RFC 7540 §5.1.2 / §6.5.2: Enforce MAX_CONCURRENT_STREAMS for new streams.
        if (!_streams.ContainsKey(streamId))
        {
            if (_activeStreamCount >= _maxConcurrentStreams)
            {
                throw new Http2Exception(
                    $"RFC 7540 §6.5.2: MAX_CONCURRENT_STREAMS limit ({_maxConcurrentStreams}) exceeded on stream {streamId}.",
                    Http2ErrorCode.RefusedStream);
            }

            _activeStreamCount++;
        }

        var data = payload;

        if ((flags & 0x8) != 0)
        {
            data = StripPadding(data, flags, padded: true);
        }

        if ((flags & 0x20) != 0)
        {
            if (data.Length < 5) return;
            data = data[5..]; // 4B Stream Dependency + 1B Weight
        }

        if ((flags & (byte)HeadersFlags.EndHeaders) != 0)
        {
            ProcessCompleteHeaders(data.Span, flags, streamId, responses);
        }
        else
        {
            _continuationStreamId = streamId;
            _continuationBufferLength = data.Length;
            _continuationFrameCount = 0;

            if (_continuationBuffer == null || _continuationBuffer.Length < data.Length)
            {
                if (_continuationBuffer != null)
                {
                    ArrayPool<byte>.Shared.Return(_continuationBuffer);
                }

                _continuationBuffer = ArrayPool<byte>.Shared.Rent(Math.Max(data.Length, 64));
            }

            data.Span.CopyTo(_continuationBuffer);
            _continuationEndStream = (flags & (byte)HeadersFlags.EndStream) != 0;
        }
    }

    private void HandleContinuation(
        ReadOnlyMemory<byte> payload,
        byte flags,
        int streamId,
        ImmutableList<(int, HttpResponseMessage)>.Builder responses)
    {
        // RFC 7540 §6.10: CONTINUATION frames MUST be associated with a stream.
        if (streamId == 0)
        {
            throw new Http2Exception(
                "RFC 7540 §6.10: CONTINUATION frame received on stream 0.",
                Http2ErrorCode.ProtocolError);
        }

        // RFC 7540 §6.10: A CONTINUATION frame MUST follow a HEADERS or PUSH_PROMISE
        // frame on the same stream.
        if (_continuationBuffer == null || streamId != _continuationStreamId)
        {
            throw new Http2Exception(
                $"RFC 7540 §6.10: CONTINUATION on stream {streamId} but expected stream {_continuationStreamId}.",
                Http2ErrorCode.ProtocolError);
        }

        // Security: reject excessive CONTINUATION frames (header-block flood protection).
        _continuationFrameCount++;
        if (_continuationFrameCount >= 1000)
        {
            throw new Http2Exception(
                $"RFC 7540 security: Excessive CONTINUATION frames ({_continuationFrameCount}) — possible header-block flood attack.",
                Http2ErrorCode.ProtocolError);
        }

        var newSize = _continuationBufferLength + payload.Length;

        if (newSize > _continuationBuffer.Length)
        {
            var newBuffer = ArrayPool<byte>.Shared.Rent(newSize);
            _continuationBuffer.AsSpan(0, _continuationBufferLength).CopyTo(newBuffer);
            ArrayPool<byte>.Shared.Return(_continuationBuffer);
            _continuationBuffer = newBuffer;
        }

        payload.Span.CopyTo(_continuationBuffer.AsSpan(_continuationBufferLength));
        _continuationBufferLength = newSize;

        if ((flags & (byte)ContinuationFlags.EndHeaders) != 0)
        {
            var endStream = _continuationEndStream;
            var headerData = _continuationBuffer.AsSpan(0, _continuationBufferLength);

            ProcessCompleteHeaders(headerData, endStream ? (byte)0x1 : (byte)0x0, _continuationStreamId, responses);

            ArrayPool<byte>.Shared.Return(_continuationBuffer);
            _continuationBuffer = null;
            _continuationBufferLength = 0;
            _continuationStreamId = 0;
            _continuationFrameCount = 0;
        }
    }

    private void HandleSettings(
        byte flags,
        ReadOnlyMemory<byte> payload,
        int payloadLength,
        ImmutableList<IReadOnlyList<(SettingsParameter, uint)>>.Builder settingsList,
        ImmutableList<Http2Frame>.Builder controlFrames,
        ImmutableList<byte[]>.Builder settingsAcksToSend)
    {
        if ((flags & (byte)SettingsFlags.Ack) != 0)
        {
            // RFC 7540 §6.5: A SETTINGS ACK frame MUST have an empty payload.
            if (payloadLength > 0)
            {
                throw new Http2Exception(
                    "RFC 7540 §6.5: SETTINGS frame with ACK flag MUST have empty payload.",
                    Http2ErrorCode.FrameSizeError);
            }
        }
        else
        {
            var settings = ParseSettings(payload.Span);
            settingsList.Add(settings);
            controlFrames.Add(new SettingsFrame(settings));
            ApplySettings(settings);
            settingsAcksToSend.Add(SettingsFrame.SettingsAck());
        }
    }

    private static void HandlePing(
        byte flags,
        ReadOnlyMemory<byte> payload,
        ImmutableList<Http2Frame>.Builder controlFrames,
        ImmutableList<byte[]>.Builder pingAcks,
        ImmutableList<byte[]>.Builder pingAcksToSend)
    {
        if ((flags & (byte)PingFlags.Ack) != 0)
        {
            pingAcks.Add(payload.ToArray());
        }
        else
        {
            controlFrames.Add(new PingFrame(payload.Span.ToArray(), isAck: false));
            // RFC 7540 §6.7: Receiver of a PING MUST send a PING ACK with the same data.
            pingAcksToSend.Add(new PingFrame(payload.Span.ToArray(), isAck: true).Serialize());
        }
    }

    private void HandleWindowUpdate(
        ReadOnlyMemory<byte> payload,
        int streamId,
        ImmutableList<(int StreamId, int Increment)>.Builder windowUpdates)
    {
        if (payload.Length < 4) return;

        var raw = BinaryPrimitives.ReadUInt32BigEndian(payload.Span);
        var increment = (int)(raw & 0x7FFFFFFFu);

        // RFC 7540 §6.9: An increment of 0 MUST be treated as PROTOCOL_ERROR.
        if (increment == 0)
        {
            throw new Http2Exception(
                "RFC 7540 §6.9: WINDOW_UPDATE increment of 0 is a PROTOCOL_ERROR.",
                Http2ErrorCode.ProtocolError);
        }

        // RFC 7540 §6.9.1: A sender MUST NOT allow a flow-control window to exceed 2^31-1.
        // Overflow is a FLOW_CONTROL_ERROR on the connection or stream.
        checked
        {
            try
            {
                if (streamId == 0)
                {
                    var newWindow = _connectionSendWindow + increment;
                    if (newWindow > 0x7FFFFFFF)
                    {
                        throw new Http2Exception(
                            $"RFC 7540 §6.9.1: WINDOW_UPDATE would overflow connection send window ({_connectionSendWindow} + {increment}).",
                            Http2ErrorCode.FlowControlError);
                    }

                    _connectionSendWindow = newWindow;
                }
            }
            catch (OverflowException)
            {
                throw new Http2Exception(
                    "RFC 7540 §6.9.1: WINDOW_UPDATE overflow on connection send window.",
                    Http2ErrorCode.FlowControlError);
            }
        }

        windowUpdates.Add((streamId, increment));
    }

    private void HandlePushPromise(
        ReadOnlyMemory<byte> payload,
        byte flags,
        int streamId,
        ImmutableList<int>.Builder promisedStreamIds)
    {
        if (payload.Length < 4) return;

        // Strip padding if PADDED flag is set (0x8).
        var data = (flags & 0x8) != 0 ? StripPadding(payload, flags, padded: true) : payload;

        if (data.Length < 4) return;

        var promisedStreamId = (int)(BinaryPrimitives.ReadUInt32BigEndian(data.Span) & 0x7FFFFFFFu);
        _promisedStreamIds.Add(promisedStreamId);
        promisedStreamIds.Add(promisedStreamId);
    }

    private void ProcessCompleteHeaders(
        ReadOnlySpan<byte> headerBlock,
        byte flags,
        int streamId,
        ImmutableList<(int, HttpResponseMessage)>.Builder responses)
    {
        var decodedHeaders = _hpack.Decode(headerBlock);
        var state = new StreamState(decodedHeaders);
        var endStream = (flags & (byte)HeadersFlags.EndStream) != 0;

        if (endStream)
        {
            _activeStreamCount--;
            _closedStreamIds.Add(streamId);
            responses.Add((streamId, state.BuildResponse()));
        }
        else
        {
            _streams[streamId] = state;
        }
    }

    private void ApplySettings(IReadOnlyList<(SettingsParameter, uint)> settings)
    {
        foreach (var (param, value) in settings)
        {
            switch (param)
            {
                case SettingsParameter.MaxFrameSize:
                    _maxFrameSize = (int)value;
                    break;

                case SettingsParameter.EnablePush:
                    // RFC 7540 §6.5.2: SETTINGS_ENABLE_PUSH MUST be 0 or 1.
                    if (value > 1)
                    {
                        throw new Http2Exception(
                            $"RFC 7540 §6.5.2: SETTINGS_ENABLE_PUSH value {value} is invalid; only 0 or 1 are permitted.",
                            Http2ErrorCode.ProtocolError);
                    }

                    break;

                case SettingsParameter.InitialWindowSize:
                    // RFC 7540 §6.5.2: Values above 2^31-1 MUST be treated as FLOW_CONTROL_ERROR.
                    if (value > 0x7FFFFFFFu)
                    {
                        throw new Http2Exception(
                            $"RFC 7540 §6.5.2: SETTINGS_INITIAL_WINDOW_SIZE value {value} exceeds maximum 2^31-1.",
                            Http2ErrorCode.FlowControlError);
                    }

                    break;

                case SettingsParameter.MaxConcurrentStreams:
                    // RFC 7540 §6.5.2: No error code is defined for violations of this limit;
                    // the decoder uses REFUSED_STREAM when the limit is exceeded.
                    _maxConcurrentStreams = (int)value;
                    break;

                default:
                    // RFC 7540 §4.1 / §6.5: Unknown or unsupported SETTINGS identifiers MUST be ignored.
                    break;
            }
        }
    }

    private static ReadOnlyMemory<byte> StripPadding(
        ReadOnlyMemory<byte> data,
        byte flags,
        bool padded)
    {
        if (!padded || data.IsEmpty) return data;
        var padLength = data.Span[0];
        if (1 + padLength > data.Length) return data;
        return data.Slice(1, data.Length - 1 - padLength);
    }

    private static List<(SettingsParameter, uint)> ParseSettings(ReadOnlySpan<byte> payload)
    {
        var result = new List<(SettingsParameter, uint)>(payload.Length / 6);
        for (var i = 0; i + 6 <= payload.Length; i += 6)
        {
            var param = (SettingsParameter)BinaryPrimitives.ReadUInt16BigEndian(payload[i..]);
            var value = BinaryPrimitives.ReadUInt32BigEndian(payload[(i + 2)..]);
            result.Add((param, value));
        }

        return result;
    }

    private static GoAwayFrame ParseGoAway(ReadOnlyMemory<byte> payload)
    {
        if (payload.Length < 8) return new GoAwayFrame(0, Http2ErrorCode.ProtocolError);
        var lastStreamId = (int)(BinaryPrimitives.ReadUInt32BigEndian(payload.Span) & 0x7FFFFFFFu);
        var errorCode = (Http2ErrorCode)BinaryPrimitives.ReadUInt32BigEndian(payload.Span[4..]);
        var debugData = payload.Length > 8 ? payload[8..].ToArray() : null;
        return new GoAwayFrame(lastStreamId, errorCode, debugData);
    }

    private static ReadOnlyMemory<byte> Combine(ReadOnlyMemory<byte> a, ReadOnlyMemory<byte> b)
    {
        if (a.IsEmpty) return b;
        if (b.IsEmpty) return a;

        var merged = new byte[a.Length + b.Length];
        a.Span.CopyTo(merged);
        b.Span.CopyTo(merged.AsSpan(a.Length));
        return merged;
    }

    private sealed class StreamState(List<HpackHeader> headers)
    {
        private byte[]? _bodyBuffer;
        private int _bodyLength;

        // RFC 7540 §5.2: Initial stream receive window size (may differ from connection default).
        public int ReceiveWindow { get; private set; } = 65535;

        public void DeductReceiveWindow(int bytes)
        {
            ReceiveWindow = Math.Max(0, ReceiveWindow - bytes);
        }

        public void SetReceiveWindow(int value)
        {
            ReceiveWindow = value;
        }

        public void AppendBody(ReadOnlySpan<byte> data)
        {
            if (data.IsEmpty) return;

            var newSize = _bodyLength + data.Length;

            if (_bodyBuffer == null || _bodyBuffer.Length < newSize)
            {
                var newBuffer = ArrayPool<byte>.Shared.Rent(newSize);
                if (_bodyBuffer != null)
                {
                    _bodyBuffer.AsSpan(0, _bodyLength).CopyTo(newBuffer);
                    ArrayPool<byte>.Shared.Return(_bodyBuffer);
                }

                _bodyBuffer = newBuffer;
            }

            data.CopyTo(_bodyBuffer.AsSpan(_bodyLength));
            _bodyLength = newSize;
        }

        public HttpResponseMessage BuildResponse()
        {
            var statusCode = HttpStatusCode.OK;
            var response = new HttpResponseMessage();

            foreach (var header in headers)
            {
                if (header.Name == ":status")
                {
                    if (int.TryParse(header.Value, out var s))
                    {
                        statusCode = (HttpStatusCode)s;
                    }

                    continue;
                }

                if (header.Name.StartsWith(':'))
                {
                    continue;
                }

                if (IsContentHeader(header.Name))
                {
                    continue;
                }

                response.Headers.TryAddWithoutValidation(header.Name, header.Value);
            }

            response.StatusCode = statusCode;

            if (_bodyLength > 0 || HasContentHeaders())
            {
                var bodyBytes = new byte[_bodyLength];
                if (_bodyBuffer != null && _bodyLength > 0)
                {
                    _bodyBuffer.AsSpan(0, _bodyLength).CopyTo(bodyBytes);
                }

                response.Content = new ByteArrayContent(bodyBytes);

                foreach (var header in headers.Where(header =>
                             !header.Name.StartsWith(':') && IsContentHeader(header.Name)))
                {
                    response.Content.Headers.TryAddWithoutValidation(header.Name, header.Value);
                }
            }

            if (_bodyBuffer == null) return response;
            ArrayPool<byte>.Shared.Return(_bodyBuffer);
            _bodyBuffer = null;

            return response;
        }

        private bool HasContentHeaders()
        {
            return headers.Any(h => !h.Name.StartsWith(':') && IsContentHeader(h.Name));
        }

        private static bool IsContentHeader(string headerName)
        {
            return headerName.ToLowerInvariant() switch
            {
                "content-type" => true,
                "content-length" => true,
                "content-encoding" => true,
                "content-language" => true,
                "content-location" => true,
                "content-md5" => true,
                "content-range" => true,
                "content-disposition" => true,
                "expires" => true,
                "last-modified" => true,
                _ => false
            };
        }
    }
}
