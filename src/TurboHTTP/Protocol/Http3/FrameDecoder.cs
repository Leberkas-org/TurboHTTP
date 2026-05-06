using System.Buffers;

namespace TurboHTTP.Protocol.Http3;

/// <summary>
/// Stateful HTTP/3 frame decoder per RFC 9114 §7.
/// Handles partial frames across QUIC stream boundaries by buffering
/// incomplete data between calls to <see cref="TryDecode"/>.
/// Unknown frame types are skipped gracefully per RFC 9114 §7.2.8.
///
/// Remainder bytes and combined working buffers are rented from <see cref="MemoryPool{T}"/>
/// to eliminate per-frame GC allocations. Frame payloads use <see cref="MemoryPool{T}"/>
/// and are returned via <see cref="IDisposable"/> on the frame objects.
/// Call <see cref="Dispose"/> when the decoder is no longer needed.
/// </summary>
internal sealed class FrameDecoder : IDisposable
{
    // MemoryPool-rented buffer holding the partial frame from the previous call.
    // _remainderOwner is null when not rented; _remainderLength tracks actual content.
    private IMemoryOwner<byte>? _remainderOwner;
    private int _remainderLength;

    // Reused per-DecodeAll-call frame list. Cleared at the start of each call.
    // Safe to reuse: Akka back-pressure guarantees all frames are consumed by downstream
    // before the next DecodeAll call.
    private readonly List<Http3Frame> _frames = [];

    /// <summary>
    /// Attempts to decode one HTTP/3 frame from <paramref name="input"/>.
    /// On <see cref="DecodeStatus.Success"/>, <paramref name="frame"/> is set and
    /// <paramref name="bytesConsumed"/> reflects the total bytes consumed from the
    /// combined remainder + input buffer.
    /// On <see cref="DecodeStatus.NeedMoreData"/>, the unconsumed data is buffered
    /// internally for the next call.
    /// </summary>
    public DecodeStatus TryDecode(ReadOnlySpan<byte> input, out Http3Frame? frame, out int bytesConsumed)
    {
        frame = null;
        bytesConsumed = 0;

        // Combine remainder with new input into a pooled working buffer
        ReadOnlySpan<byte> data;
        IMemoryOwner<byte>? rentedCombined = null;
        var combinedLength = 0;

        if (_remainderLength > 0)
        {
            combinedLength = _remainderLength + input.Length;
            rentedCombined = MemoryPool<byte>.Shared.Rent(combinedLength);
            _remainderOwner!.Memory.Span[.._remainderLength].CopyTo(rentedCombined.Memory.Span);
            input.CopyTo(rentedCombined.Memory.Span[_remainderLength..]);
            data = rentedCombined.Memory.Span[..combinedLength];

            // Dispose old remainder buffer now that its content has been copied out
            _remainderOwner?.Dispose();
            _remainderOwner = null;
            _remainderLength = 0;
        }
        else
        {
            data = input;
        }

        try
        {
            var result = TryDecodeFrame(data, out frame, out var totalConsumed);

            if (result == DecodeStatus.NeedMoreData)
            {
                // Buffer unconsumed data for next call
                if (data.Length > 0)
                {
                    _remainderOwner = MemoryPool<byte>.Shared.Rent(data.Length);
                    data.CopyTo(_remainderOwner.Memory.Span);
                    _remainderLength = data.Length;
                }

                bytesConsumed = input.Length; // All input consumed (buffered)
                return DecodeStatus.NeedMoreData;
            }

            // Calculate how many bytes of the original input were consumed
            if (rentedCombined != null)
            {
                // All input bytes are accounted for: some went into the decoded frame
                // (together with the old remainder), the rest is buffered as the new remainder.
                // Returning input.Length prevents DecodeAll from re-passing bytes that are
                // already captured in the remainder — avoiding double-counting corruption.
                bytesConsumed = input.Length;

                // Buffer any leftover from combined
                var leftover = combinedLength - totalConsumed;
                if (leftover > 0)
                {
                    _remainderOwner = MemoryPool<byte>.Shared.Rent(leftover);
                    rentedCombined.Memory.Span.Slice(totalConsumed, leftover).CopyTo(_remainderOwner.Memory.Span);
                    _remainderLength = leftover;
                }
            }
            else
            {
                bytesConsumed = totalConsumed;
            }

            return DecodeStatus.Success;
        }
        finally
        {
            rentedCombined?.Dispose();
        }
    }

    /// <summary>
    /// Attempts to decode all available frames from <paramref name="input"/>.
    /// Returns the list of decoded frames and the total bytes consumed from the input.
    /// Any trailing partial frame is buffered for the next call.
    /// </summary>
    public IReadOnlyList<Http3Frame> DecodeAll(ReadOnlySpan<byte> input, out int bytesConsumed)
    {
        _frames.Clear();
        bytesConsumed = 0;

        while (true)
        {
            var status = TryDecode(input[bytesConsumed..], out var frame, out var consumed);

            if (status == DecodeStatus.NeedMoreData)
            {
                break;
            }

            bytesConsumed += consumed;

            // Skip null frames (unknown frame types silently ignored per RFC 9114 §7.2.8)
            if (frame is not null)
            {
                _frames.Add(frame);
            }
        }

        return _frames;
    }

    /// <summary>
    /// Resets the decoder state, discarding any buffered partial frame data.
    /// </summary>
    public void Reset()
    {
        _remainderOwner?.Dispose();
        _remainderOwner = null;
        _remainderLength = 0;
    }

    /// <summary>
    /// Disposes the decoder, returning any pooled remainder buffer to the pool.
    /// </summary>
    public void Dispose() => Reset();

    /// <summary>
    /// Returns <c>true</c> if the decoder has buffered partial frame data.
    /// </summary>
    public bool HasRemainder => _remainderLength > 0;

    private static DecodeStatus TryDecodeFrame(
        ReadOnlySpan<byte> data,
        out Http3Frame? frame,
        out int totalConsumed)
    {
        frame = null;
        totalConsumed = 0;

        // Decode frame type (QUIC varint)
        if (!QuicVarInt.TryDecode(data, out var rawType, out var typeBytes))
        {
            return DecodeStatus.NeedMoreData;
        }

        // Decode frame length (QUIC varint)
        if (!QuicVarInt.TryDecode(data[typeBytes..], out var payloadLength, out var lengthBytes))
        {
            return DecodeStatus.NeedMoreData;
        }

        var headerSize = typeBytes + lengthBytes;

        if (payloadLength > int.MaxValue - headerSize)
        {
            throw new Http3Exception(ErrorCode.FrameError,
                $"HTTP/3 frame payload length {payloadLength} exceeds maximum decodable size.");
        }

        var frameSize = headerSize + (int)payloadLength;

        // Need more data for the payload
        if (data.Length < frameSize)
        {
            return DecodeStatus.NeedMoreData;
        }

        var payload = data.Slice(headerSize, (int)payloadLength);
        totalConsumed = frameSize;

        // Parse frame by type
        if (!Enum.IsDefined((FrameType)rawType))
        {
            // Unknown frame type — skip gracefully per RFC 9114 §7.2.8
            // Return a success with null frame to indicate skipped unknown frame
            frame = null;

            // We still consumed the bytes, but we need to signal this differently.
            // Use a sentinel: return Success but with frame = null means "skipped unknown type".
            // The caller can check frame == null to detect this.
            return DecodeStatus.Success;
        }

        var frameType = (FrameType)rawType;

        frame = frameType switch
        {
            FrameType.Data => DecodeDataFrame(payload),
            FrameType.Headers => DecodeHeadersFrame(payload),
            FrameType.CancelPush => DecodeCancelPushFrame(payload),
            FrameType.Settings => DecodeSettingsFrame(payload),
            FrameType.PushPromise => DecodePushPromiseFrame(payload),
            FrameType.GoAway => DecodeGoAwayFrame(payload),
            FrameType.MaxPushId => DecodeMaxPushIdFrame(payload),
            _ => null, // Should not happen given IsDefined check above
        };

        return DecodeStatus.Success;
    }

    private static DataFrame DecodeDataFrame(ReadOnlySpan<byte> payload)
    {
        if (payload.Length == 0)
        {
            return new DataFrame(ReadOnlyMemory<byte>.Empty);
        }

        var owner = MemoryPool<byte>.Shared.Rent(payload.Length);
        payload.CopyTo(owner.Memory.Span);
        return new DataFrame(owner, payload.Length);
    }

    private static HeadersFrame DecodeHeadersFrame(ReadOnlySpan<byte> payload)
    {
        if (payload.Length == 0)
        {
            return new HeadersFrame(ReadOnlyMemory<byte>.Empty);
        }

        var owner = MemoryPool<byte>.Shared.Rent(payload.Length);
        payload.CopyTo(owner.Memory.Span);
        return new HeadersFrame(owner, payload.Length);
    }

    private static CancelPushFrame DecodeCancelPushFrame(ReadOnlySpan<byte> payload)
    {
        var pushId = QuicVarInt.Decode(payload, out _);
        return new CancelPushFrame(pushId);
    }

    private static SettingsFrame DecodeSettingsFrame(ReadOnlySpan<byte> payload)
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

        return new SettingsFrame(parameters);
    }

    private static PushPromiseFrame DecodePushPromiseFrame(ReadOnlySpan<byte> payload)
    {
        var pushId = QuicVarInt.Decode(payload, out var pushIdBytes);
        var headerBlockSpan = payload[pushIdBytes..];

        if (headerBlockSpan.Length == 0)
        {
            return new PushPromiseFrame(pushId, ReadOnlyMemory<byte>.Empty);
        }

        var owner = MemoryPool<byte>.Shared.Rent(headerBlockSpan.Length);
        headerBlockSpan.CopyTo(owner.Memory.Span);
        return new PushPromiseFrame(pushId, owner, headerBlockSpan.Length);
    }

    private static GoAwayFrame DecodeGoAwayFrame(ReadOnlySpan<byte> payload)
    {
        var streamId = QuicVarInt.Decode(payload, out _);
        return new GoAwayFrame(streamId);
    }

    private static MaxPushIdFrame DecodeMaxPushIdFrame(ReadOnlySpan<byte> payload)
    {
        var pushId = QuicVarInt.Decode(payload, out _);
        return new MaxPushIdFrame(pushId);
    }
}
