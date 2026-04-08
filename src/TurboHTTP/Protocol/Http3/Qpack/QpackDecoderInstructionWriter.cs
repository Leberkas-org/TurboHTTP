namespace TurboHTTP.Protocol.Http3.Qpack;

/// <summary>
/// RFC 9204 §4.4 — Writes decoder instructions to the decoder stream.
///
/// Decoder instructions are sent from decoder to encoder as feedback:
///   - Section Acknowledgment (§4.4.1)
///   - Stream Cancellation (§4.4.2)
///   - Insert Count Increment (§4.4.3)
/// </summary>
public static class QpackDecoderInstructionWriter
{
    /// <summary>
    /// RFC 9204 §4.4.1 — Section Acknowledgment.
    ///
    ///   0   1   2   3   4   5   6   7
    /// +---+---+---+---+---+---+---+---+
    /// | 1 |   Stream ID (7+)          |
    /// +---+---------------------------+
    ///
    /// Acknowledges processing of a header block on the given stream,
    /// allowing the encoder to evict referenced dynamic table entries.
    /// </summary>
    /// <param name="streamId">The stream ID of the acknowledged header block (must be non-negative).</param>
    /// <param name="output">Destination span (sliced on return to exclude written bytes).</param>
    /// <returns>Number of bytes written.</returns>
    public static int WriteSectionAcknowledgment(int streamId, ref Span<byte> output)
    {
        if (streamId < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(streamId), "Stream ID must be non-negative.");
        }

        // Prefix: 1xxxxxxx → prefixFlags = 0x80, prefixBits = 7
        return QpackIntegerCodec.Encode(streamId, 7, 0x80, ref output);
    }

    /// <summary>
    /// RFC 9204 §4.4.2 — Stream Cancellation.
    ///
    ///   0   1   2   3   4   5   6   7
    /// +---+---+---+---+---+---+---+---+
    /// | 0 | 1 |  Stream ID (6+)       |
    /// +---+---+-----------------------+
    ///
    /// Signals that the decoder will not process the header block on the given stream,
    /// allowing the encoder to evict referenced entries.
    /// </summary>
    /// <param name="streamId">The stream ID being cancelled (must be non-negative).</param>
    /// <param name="output">Destination span (sliced on return to exclude written bytes).</param>
    /// <returns>Number of bytes written.</returns>
    public static int WriteStreamCancellation(int streamId, ref Span<byte> output)
    {
        if (streamId < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(streamId), "Stream ID must be non-negative.");
        }

        // Prefix: 01xxxxxx → prefixFlags = 0x40, prefixBits = 6
        return QpackIntegerCodec.Encode(streamId, 6, 0x40, ref output);
    }

    /// <summary>
    /// RFC 9204 §4.4.3 — Insert Count Increment.
    ///
    ///   0   1   2   3   4   5   6   7
    /// +---+---+---+---+---+---+---+---+
    /// | 0 | 0 |   Increment (6+)      |
    /// +---+---+-----------------------+
    ///
    /// Increases the Known Received Count, informing the encoder that
    /// the decoder has received additional dynamic table insertions.
    /// </summary>
    /// <param name="increment">The increment value (must be positive).</param>
    /// <param name="output">Destination span (sliced on return to exclude written bytes).</param>
    /// <returns>Number of bytes written.</returns>
    public static int WriteInsertCountIncrement(int increment, ref Span<byte> output)
    {
        if (increment <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(increment), "Increment must be positive.");
        }

        // Prefix: 00xxxxxx → prefixFlags = 0x00, prefixBits = 6
        return QpackIntegerCodec.Encode(increment, 6, 0x00, ref output);
    }
}
