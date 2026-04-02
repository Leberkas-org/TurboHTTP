namespace TurboHttp.Protocol.Http3;

/// <summary>
/// Result of a frame decode attempt.
/// </summary>
public enum Http3DecodeStatus
{
    /// <summary>A complete frame was decoded.</summary>
    Success,

    /// <summary>Not enough data to decode a complete frame; feed more bytes.</summary>
    NeedMoreData,
}

/// <summary>
/// Stateful HTTP/3 frame decoder per RFC 9114 §7.
/// Handles partial frames across QUIC stream boundaries by buffering
/// incomplete data between calls to <see cref="TryDecode"/>.
/// Unknown frame types are skipped gracefully per RFC 9114 §7.2.8.
/// </summary>
public sealed class Http3FrameDecoder
{
    private byte[] _remainder = [];

    /// <summary>
    /// Attempts to decode one HTTP/3 frame from <paramref name="input"/>.
    /// On <see cref="Http3DecodeStatus.Success"/>, <paramref name="frame"/> is set and
    /// <paramref name="bytesConsumed"/> reflects the total bytes consumed from the
    /// combined remainder + input buffer.
    /// On <see cref="Http3DecodeStatus.NeedMoreData"/>, the unconsumed data is buffered
    /// internally for the next call.
    /// </summary>
    public Http3DecodeStatus TryDecode(ReadOnlySpan<byte> input, out Http3Frame? frame, out int bytesConsumed)
    {
        frame = null;
        bytesConsumed = 0;

        // Combine remainder with new input
        ReadOnlySpan<byte> data;
        byte[]? combined = null;

        if (_remainder.Length > 0)
        {
            combined = new byte[_remainder.Length + input.Length];
            _remainder.CopyTo(combined, 0);
            input.CopyTo(combined.AsSpan(_remainder.Length));
            data = combined;
            _remainder = [];
        }
        else
        {
            data = input;
        }

        var result = TryDecodeFrame(data, out frame, out var totalConsumed);

        if (result == Http3DecodeStatus.NeedMoreData)
        {
            // Buffer unconsumed data for next call
            if (data.Length > 0)
            {
                _remainder = data.ToArray();
            }

            bytesConsumed = input.Length; // All input consumed (buffered)
            return Http3DecodeStatus.NeedMoreData;
        }

        // Calculate how many bytes of the original input were consumed
        if (combined != null)
        {
            var remainderUsed = Math.Min(totalConsumed, combined.Length - input.Length);
            bytesConsumed = totalConsumed - remainderUsed;

            // Buffer any leftover from combined
            var leftover = combined.Length - totalConsumed;
            if (leftover > 0)
            {
                _remainder = new byte[leftover];
                combined.AsSpan(totalConsumed, leftover).CopyTo(_remainder);
            }
        }
        else
        {
            bytesConsumed = totalConsumed;
        }

        return Http3DecodeStatus.Success;
    }

    /// <summary>
    /// Attempts to decode all available frames from <paramref name="input"/>.
    /// Returns the list of decoded frames and the total bytes consumed from the input.
    /// Any trailing partial frame is buffered for the next call.
    /// </summary>
    public IReadOnlyList<Http3Frame> DecodeAll(ReadOnlySpan<byte> input, out int bytesConsumed)
    {
        var frames = new List<Http3Frame>();
        bytesConsumed = 0;

        while (true)
        {
            var status = TryDecode(input[bytesConsumed..], out var frame, out var consumed);

            if (status == Http3DecodeStatus.NeedMoreData)
            {
                break;
            }

            bytesConsumed += consumed;

            // Skip null frames (unknown frame types silently ignored per RFC 9114 §7.2.8)
            if (frame is not null)
            {
                frames.Add(frame);
            }
        }

        return frames;
    }

    /// <summary>
    /// Resets the decoder state, discarding any buffered partial frame data.
    /// </summary>
    public void Reset()
    {
        _remainder = [];
    }

    /// <summary>
    /// Returns <c>true</c> if the decoder has buffered partial frame data.
    /// </summary>
    public bool HasRemainder => _remainder.Length > 0;

    private static Http3DecodeStatus TryDecodeFrame(
        ReadOnlySpan<byte> data,
        out Http3Frame? frame,
        out int totalConsumed)
    {
        frame = null;
        totalConsumed = 0;

        // Decode frame type (QUIC varint)
        if (!QuicVarInt.TryDecode(data, out var rawType, out var typeBytes))
        {
            return Http3DecodeStatus.NeedMoreData;
        }

        // Decode frame length (QUIC varint)
        if (!QuicVarInt.TryDecode(data[typeBytes..], out var payloadLength, out var lengthBytes))
        {
            return Http3DecodeStatus.NeedMoreData;
        }

        var headerSize = typeBytes + lengthBytes;
        var frameSize = headerSize + (int)payloadLength;

        // Need more data for the payload
        if (data.Length < frameSize)
        {
            return Http3DecodeStatus.NeedMoreData;
        }

        var payload = data.Slice(headerSize, (int)payloadLength);
        totalConsumed = frameSize;

        // Parse frame by type
        if (!Enum.IsDefined((Http3FrameType)rawType))
        {
            // Unknown frame type — skip gracefully per RFC 9114 §7.2.8
            // Return a success with null frame to indicate skipped unknown frame
            frame = null;

            // We still consumed the bytes, but we need to signal this differently.
            // Use a sentinel: return Success but with frame = null means "skipped unknown type".
            // The caller can check frame == null to detect this.
            return Http3DecodeStatus.Success;
        }

        var frameType = (Http3FrameType)rawType;

        frame = frameType switch
        {
            Http3FrameType.Data => DecodeDataFrame(payload),
            Http3FrameType.Headers => DecodeHeadersFrame(payload),
            Http3FrameType.CancelPush => DecodeCancelPushFrame(payload),
            Http3FrameType.Settings => DecodeSettingsFrame(payload),
            Http3FrameType.PushPromise => DecodePushPromiseFrame(payload),
            Http3FrameType.GoAway => DecodeGoAwayFrame(payload),
            Http3FrameType.MaxPushId => DecodeMaxPushIdFrame(payload),
            _ => null, // Should not happen given IsDefined check above
        };

        return Http3DecodeStatus.Success;
    }

    private static Http3DataFrame DecodeDataFrame(ReadOnlySpan<byte> payload)
    {
        return new Http3DataFrame(payload.ToArray());
    }

    private static Http3HeadersFrame DecodeHeadersFrame(ReadOnlySpan<byte> payload)
    {
        return new Http3HeadersFrame(payload.ToArray());
    }

    private static Http3CancelPushFrame DecodeCancelPushFrame(ReadOnlySpan<byte> payload)
    {
        var pushId = QuicVarInt.Decode(payload, out _);
        return new Http3CancelPushFrame(pushId);
    }

    private static Http3SettingsFrame DecodeSettingsFrame(ReadOnlySpan<byte> payload)
    {
        var parameters = new List<(long Identifier, long Value)>();
        var offset = 0;

        while (offset < payload.Length)
        {
            var id = QuicVarInt.Decode(payload[offset..], out var idBytes);
            offset += idBytes;

            var value = QuicVarInt.Decode(payload[offset..], out var valBytes);
            offset += valBytes;

            parameters.Add((id, value));
        }

        return new Http3SettingsFrame(parameters);
    }

    private static Http3PushPromiseFrame DecodePushPromiseFrame(ReadOnlySpan<byte> payload)
    {
        var pushId = QuicVarInt.Decode(payload, out var pushIdBytes);
        var headerBlock = payload[pushIdBytes..].ToArray();
        return new Http3PushPromiseFrame(pushId, headerBlock);
    }

    private static Http3GoAwayFrame DecodeGoAwayFrame(ReadOnlySpan<byte> payload)
    {
        var streamId = QuicVarInt.Decode(payload, out _);
        return new Http3GoAwayFrame(streamId);
    }

    private static Http3MaxPushIdFrame DecodeMaxPushIdFrame(ReadOnlySpan<byte> payload)
    {
        var pushId = QuicVarInt.Decode(payload, out _);
        return new Http3MaxPushIdFrame(pushId);
    }
}