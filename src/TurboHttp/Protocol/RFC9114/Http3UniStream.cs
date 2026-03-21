using System;
using TurboHttp.Protocol.RFC9000;

namespace TurboHttp.Protocol.RFC9114;

// HTTP/3 Unidirectional Stream Handling  —  RFC 9114 §6.2
//
// Unidirectional streams carry data in one direction: from the initiator
// to the receiver. The stream type is indicated by a variable-length integer
// at the start of the stream. Known types are Control (0x00), Push (0x01),
// QPACK Encoder (0x02), and QPACK Decoder (0x03).
//
// Implementations MUST ignore unidirectional streams with unknown types
// (RFC 9114 §6.2): "If the stream type is not supported by the receiver,
// the remainder of the stream cannot be consumed [...] recipients of
// unknown stream types MUST either abort reading of the stream or discard
// incoming data."

/// <summary>
/// Result of identifying a server-initiated unidirectional stream.
/// </summary>
public enum UniStreamRouting
{
    /// <summary>Routed to the control stream handler.</summary>
    Control,

    /// <summary>Routed to the push stream handler.</summary>
    Push,

    /// <summary>Routed to the QPACK encoder stream handler.</summary>
    QpackEncoder,

    /// <summary>Routed to the QPACK decoder stream handler.</summary>
    QpackDecoder,

    /// <summary>Unknown stream type — MUST be ignored per RFC 9114 §6.2.</summary>
    Unknown,
}

/// <summary>
/// Handles identification and routing of HTTP/3 unidirectional streams
/// per RFC 9114 §6.2. Reads the stream type varint from the beginning of
/// a unidirectional stream and routes to the appropriate handler.
/// </summary>
public sealed class Http3UniStream
{
    private bool _controlStreamReceived;
    private bool _qpackEncoderStreamReceived;
    private bool _qpackDecoderStreamReceived;

    /// <summary>Whether a server control stream has been received.</summary>
    public bool ControlStreamReceived => _controlStreamReceived;

    /// <summary>Whether a server QPACK encoder stream has been received.</summary>
    public bool QpackEncoderStreamReceived => _qpackEncoderStreamReceived;

    /// <summary>Whether a server QPACK decoder stream has been received.</summary>
    public bool QpackDecoderStreamReceived => _qpackDecoderStreamReceived;

    /// <summary>
    /// Attempts to identify the stream type from the initial bytes of
    /// a server-initiated unidirectional stream.
    /// </summary>
    /// <param name="data">
    /// The initial bytes of the unidirectional stream, starting with
    /// the stream type varint.
    /// </param>
    /// <param name="routing">The routing result for this stream.</param>
    /// <param name="streamType">The raw stream type value decoded from the varint.</param>
    /// <param name="bytesConsumed">
    /// Number of bytes consumed for the stream type varint.
    /// </param>
    /// <returns>
    /// <c>true</c> if the stream type was successfully decoded;
    /// <c>false</c> if the buffer is too short to decode the varint.
    /// </returns>
    /// <exception cref="Http3ConnectionException">
    /// Thrown with <see cref="Http3ErrorCode.StreamCreationError"/> if a
    /// duplicate critical stream (control, QPACK encoder, or QPACK decoder)
    /// is received.
    /// </exception>
    public bool TryIdentify(
        ReadOnlySpan<byte> data,
        out UniStreamRouting routing,
        out long streamType,
        out int bytesConsumed)
    {
        routing = UniStreamRouting.Unknown;
        streamType = -1;
        bytesConsumed = 0;

        if (!QuicVarInt.TryDecode(data, out var typeValue, out var consumed))
        {
            return false;
        }

        streamType = typeValue;
        bytesConsumed = consumed;

        if (typeValue == (long)Http3StreamType.Control)
        {
            if (_controlStreamReceived)
            {
                throw new Http3ConnectionException(
                    Http3ErrorCode.StreamCreationError,
                    "Receiving a second control stream is a connection error (RFC 9114 §6.2.1).");
            }

            _controlStreamReceived = true;
            routing = UniStreamRouting.Control;
            return true;
        }

        if (typeValue == (long)Http3StreamType.Push)
        {
            routing = UniStreamRouting.Push;
            return true;
        }

        if (typeValue == (long)Http3StreamType.QpackEncoder)
        {
            if (_qpackEncoderStreamReceived)
            {
                throw new Http3ConnectionException(
                    Http3ErrorCode.StreamCreationError,
                    "Receiving a second QPACK encoder stream is a connection error (RFC 9114 §6.2.1).");
            }

            _qpackEncoderStreamReceived = true;
            routing = UniStreamRouting.QpackEncoder;
            return true;
        }

        if (typeValue == (long)Http3StreamType.QpackDecoder)
        {
            if (_qpackDecoderStreamReceived)
            {
                throw new Http3ConnectionException(
                    Http3ErrorCode.StreamCreationError,
                    "Receiving a second QPACK decoder stream is a connection error (RFC 9114 §6.2.1).");
            }

            _qpackDecoderStreamReceived = true;
            routing = UniStreamRouting.QpackDecoder;
            return true;
        }

        // Unknown stream type — MUST be ignored (RFC 9114 §6.2)
        routing = UniStreamRouting.Unknown;
        return true;
    }
}
