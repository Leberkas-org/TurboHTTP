using TurboHTTP.Internal;
using TurboHTTP.Protocol.Http3.Qpack;
using TurboHTTP.Streams.Stages;

namespace TurboHTTP.Protocol.Http3;

/// <summary>
/// Manages QPACK encoder/decoder instruction streams for an HTTP/3 connection.
/// Handles preface emission, instruction serialization, blocked stream resolution,
/// and Insert Count Increment bookkeeping (RFC 9204 §4.4).
/// </summary>
internal sealed class QpackStreamHandler
{
    private readonly IStageOperations _ops;
    private readonly RequestEncoder _requestEncoder;
    private readonly ResponseDecoder _responseDecoder;
    private readonly QpackTableSync _tableSync;

    private bool _encoderPrefaceSent;
    private bool _decoderPrefaceSent;

    public QpackStreamHandler(
        IStageOperations ops,
        RequestEncoder requestEncoder,
        ResponseDecoder responseDecoder,
        QpackTableSync tableSync)
    {
        _ops = ops;
        _requestEncoder = requestEncoder;
        _responseDecoder = responseDecoder;
        _tableSync = tableSync;
    }

    /// <summary>
    /// Processes bytes from the inbound QPACK decoder stream.
    /// Forwards decoder instructions (Section Ack, ICR, Stream Cancellation) to the
    /// encoder so its Known Received Count stays accurate (RFC 9204 §4.4).
    /// </summary>
    public void ProcessDecoderBytes(ReadOnlyMemory<byte> data)
    {
        try
        {
            _tableSync.ProcessDecoderInstructions(data.Span);
        }
        catch (Exception ex)
        {
            _ops.OnWarning($"QPACK decoder stream error absorbed — {ex.Message}");
        }
    }

    /// <summary>
    /// Processes bytes from the inbound QPACK encoder stream.
    /// Applies encoder instructions to the decoder's dynamic table,
    /// resolves any blocked streams (RFC 9204 §2.1.2), and emits an
    /// Insert Count Increment on the decoder stream (RFC 9204 §4.4.3).
    /// Returns resolved (streamId, headers) pairs for response assembly.
    /// </summary>
    public IReadOnlyList<(int StreamId, IReadOnlyList<(string Name, string Value)> Headers)> ProcessEncoderBytes(
        ReadOnlyMemory<byte> data)
    {
        try
        {
            _tableSync.ApplyEncoderInstructions(data.Span);
            return _tableSync.ResolveBlockedStreams();
        }
        catch (Exception ex)
        {
            _ops.OnWarning($"QPACK encoder stream error absorbed — {ex.Message}");
            return [];
        }
    }

    /// <summary>
    /// Serializes pending QPACK decoder instructions (Section Acknowledgments
    /// and Insert Count Increments) and emits them on the decoder instruction stream.
    /// Prepends the stream type prefix (VarInt 0x03) on first emission.
    /// RFC 9204 §4.4.
    /// </summary>
    public void FlushDecoderInstructions(RequestEndpoint endpoint)
    {
        var sectionAck = _responseDecoder.DecoderInstructions;

        var buf = RoutedNetworkBuffer.Rent(1 + sectionAck.Length + 16);
        var dest = buf.FullMemory.Span;
        var offset = 0;

        if (!_decoderPrefaceSent)
        {
            dest[offset++] = 0x03;
        }

        if (sectionAck.Length > 0)
        {
            sectionAck.Span.CopyTo(dest[offset..]);
            offset += sectionAck.Length;
        }

        var icrSpan = dest[offset..];
        var icrAvailable = icrSpan.Length;
        _tableSync.WriteInsertCountIncrement(ref icrSpan);
        offset += icrAvailable - icrSpan.Length;

        if (offset == 0 || (offset == 1 && !_decoderPrefaceSent))
        {
            buf.Dispose();
            return;
        }

        _decoderPrefaceSent = true;
        buf.Length = offset;
        buf.Key = endpoint;
        buf.StreamTypeValue = 0x03;
        _ops.OnOutbound(buf);
    }

    /// <summary>
    /// Serializes any pending QPACK encoder instructions and emits them
    /// as tagged items on the encoder stream. Prepends the stream type prefix
    /// (VarInt 0x02) on first emission.
    /// </summary>
    public void FlushEncoderInstructions(RequestEndpoint endpoint)
    {
        var instructions = _requestEncoder.EncoderInstructions;
        if (instructions.Length == 0)
        {
            return;
        }

        int totalLength;
        using var owner = System.Buffers.MemoryPool<byte>.Shared.Rent(1 + instructions.Length);
        var span = owner.Memory.Span;

        if (!_encoderPrefaceSent)
        {
            _encoderPrefaceSent = true;
            span[0] = 0x02;
            instructions.Span.CopyTo(span[1..]);
            totalLength = 1 + instructions.Length;
        }
        else
        {
            instructions.Span.CopyTo(span);
            totalLength = instructions.Length;
        }

        var buf = RoutedNetworkBuffer.Rent(totalLength);
        owner.Memory.Span[..totalLength].CopyTo(buf.FullMemory.Span);
        buf.Length = totalLength;
        buf.Key = endpoint;
        buf.StreamTypeValue = 0x02;

        _ops.OnOutbound(buf);
    }

    /// <summary>
    /// Resets preface tracking for a new connection (after reconnect).
    /// </summary>
    public void Reset()
    {
        _encoderPrefaceSent = false;
        _decoderPrefaceSent = false;
    }
}
