using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using TurboHttp.Protocol.RFC9204;

namespace TurboHttp.Protocol.RFC9114;

/// <summary>
/// RFC 9114 §4.1 — Decodes HTTP/3 frames into HTTP response messages.
/// Uses QPACK (RFC 9204) for header decompression.
///
/// Processes a sequence of HTTP/3 frames received on a request stream:
///   1. HEADERS frame → response headers (decoded via QPACK)
///   2. DATA frames → response body
///   3. Optional trailing HEADERS frame → trailers
///
/// Stateful: maintains a <see cref="QpackDecoder"/> instance for header decompression.
/// One instance per connection.
/// </summary>
public sealed class Http3ResponseDecoder
{
    private readonly QpackDecoder _qpack;

    /// <summary>
    /// Creates a new HTTP/3 response decoder.
    /// </summary>
    /// <param name="maxTableCapacity">
    /// Maximum QPACK dynamic table capacity in bytes (SETTINGS_QPACK_MAX_TABLE_CAPACITY).
    /// Set to 0 to disable dynamic table usage.
    /// </param>
    /// <param name="maxBlockedStreams">
    /// Maximum number of streams that may be blocked waiting for dynamic table updates
    /// (SETTINGS_QPACK_BLOCKED_STREAMS). Default 100.
    /// </param>
    public Http3ResponseDecoder(int maxTableCapacity = 4096, int maxBlockedStreams = 100)
    {
        _qpack = new QpackDecoder(maxTableCapacity, maxBlockedStreams);
    }

    /// <summary>
    /// The underlying QPACK decoder (for inspection and testing).
    /// </summary>
    public QpackDecoder QpackDecoder => _qpack;

    /// <summary>
    /// Decoder instructions emitted during the most recent <see cref="Decode"/> call.
    /// These must be sent on the QPACK decoder instruction stream (unidirectional stream
    /// type 0x03) after processing the response.
    /// </summary>
    public ReadOnlyMemory<byte> DecoderInstructions => _qpack.DecoderInstructions;

    /// <summary>
    /// Decodes a sequence of HTTP/3 frames into an <see cref="HttpResponseMessage"/>.
    ///
    /// Expects at least one HEADERS frame followed by zero or more DATA frames.
    /// The HEADERS frame contains the QPACK-compressed response headers including
    /// the :status pseudo-header.
    ///
    /// After calling this method, check <see cref="DecoderInstructions"/> for any
    /// QPACK decoder instructions that must be sent on the decoder stream.
    /// </summary>
    /// <param name="frames">The HTTP/3 frames received on the request stream.</param>
    /// <param name="streamId">
    /// The QUIC stream ID (used for QPACK Section Acknowledgment).
    /// </param>
    /// <returns>The decoded HTTP response message.</returns>
    public HttpResponseMessage Decode(IReadOnlyList<Http3Frame> frames, int streamId = 0)
    {
        ArgumentNullException.ThrowIfNull(frames);

        if (frames.Count == 0)
        {
            throw new Http3ConnectionException(Http3ErrorCode.FrameUnexpected,
                "RFC 9114 §4.1: No frames received on request stream.");
        }

        // First frame must be HEADERS
        if (frames[0] is not Http3HeadersFrame headersFrame)
        {
            throw new Http3ConnectionException(Http3ErrorCode.FrameUnexpected,
                $"RFC 9114 §4.1: Expected HEADERS frame, got {frames[0].Type}.");
        }

        // Decode headers via QPACK
        var headers = _qpack.Decode(headersFrame.HeaderBlock.Span, streamId);

        // Validate and extract :status pseudo-header
        ValidateResponsePseudoHeaders(headers);
        var statusCode = ExtractStatusCode(headers);

        var response = new HttpResponseMessage((HttpStatusCode)statusCode);

        // Add regular headers to response
        foreach (var (name, value) in headers)
        {
            if (name.StartsWith(':'))
            {
                continue; // Skip pseudo-headers
            }

            // Content headers go to Content.Headers, others to response.Headers
            if (IsContentHeader(name))
            {
                response.Content ??= new ByteArrayContent([]);
                response.Content.Headers.TryAddWithoutValidation(name, value);
            }
            else
            {
                response.Headers.TryAddWithoutValidation(name, value);
            }
        }

        // Assemble body from DATA frames
        var bodyParts = new List<byte[]>();
        var totalLength = 0;

        for (var i = 1; i < frames.Count; i++)
        {
            if (frames[i] is Http3DataFrame dataFrame)
            {
                var data = dataFrame.Data.ToArray();
                bodyParts.Add(data);
                totalLength += data.Length;
            }
            else if (frames[i] is Http3HeadersFrame)
            {
                // Trailing HEADERS frame (trailers) — skip for now
                break;
            }
        }

        if (totalLength > 0)
        {
            var body = new byte[totalLength];
            var offset = 0;
            foreach (var part in bodyParts)
            {
                part.CopyTo(body, offset);
                offset += part.Length;
            }

            response.Content = new ByteArrayContent(body);
        }

        return response;
    }

    /// <summary>
    /// Decodes a single HEADERS frame's QPACK header block into header field pairs.
    /// Useful for inspecting raw decoded headers without constructing an HttpResponseMessage.
    /// </summary>
    /// <param name="headersFrame">The HEADERS frame to decode.</param>
    /// <param name="streamId">The QUIC stream ID for Section Acknowledgment.</param>
    /// <returns>The decoded header fields as (name, value) pairs.</returns>
    public IReadOnlyList<(string Name, string Value)> DecodeHeaders(Http3HeadersFrame headersFrame, int streamId = 0)
    {
        ArgumentNullException.ThrowIfNull(headersFrame);
        return _qpack.Decode(headersFrame.HeaderBlock.Span, streamId);
    }

    /// <summary>
    /// Validates response pseudo-headers per RFC 9114 §4.3.2:
    /// - Only :status is allowed as a pseudo-header
    /// - Must appear before regular headers
    /// - No duplicates
    /// - No unknown pseudo-headers
    /// </summary>
    internal static void ValidateResponsePseudoHeaders(IReadOnlyList<(string Name, string Value)> headers)
    {
        var hasStatus = false;
        var lastPseudoIndex = -1;
        var firstRegularIndex = int.MaxValue;

        for (var i = 0; i < headers.Count; i++)
        {
            var (name, _) = headers[i];

            if (name.StartsWith(':'))
            {
                lastPseudoIndex = i;

                if (name == ":status")
                {
                    if (hasStatus)
                    {
                        throw new Http3ConnectionException(Http3ErrorCode.MessageError,
                            "RFC 9114 §4.3.2: Duplicate :status pseudo-header");
                    }
                    hasStatus = true;
                }
                else
                {
                    throw new Http3ConnectionException(Http3ErrorCode.MessageError,
                        $"RFC 9114 §4.3.2: Unknown response pseudo-header '{name}'");
                }
            }
            else
            {
                if (firstRegularIndex == int.MaxValue)
                {
                    firstRegularIndex = i;
                }
            }
        }

        if (lastPseudoIndex > firstRegularIndex)
        {
            throw new Http3ConnectionException(Http3ErrorCode.MessageError,
                $"RFC 9114 §4.3.2: Pseudo-header at index {lastPseudoIndex} appears after regular header at index {firstRegularIndex}");
        }
    }

    /// <summary>
    /// Extracts the :status pseudo-header from the decoded header list.
    /// RFC 9114 §4.3.2: Response pseudo-headers consist of a single :status field.
    /// </summary>
    private static int ExtractStatusCode(IReadOnlyList<(string Name, string Value)> headers)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            if (headers[i].Name == ":status")
            {
                if (!int.TryParse(headers[i].Value, out var code) || code < 100 || code > 999)
                {
                    throw new Http3ConnectionException(Http3ErrorCode.MessageError,
                        $"RFC 9114 §4.3.2: Invalid :status value '{headers[i].Value}'.");
                }

                return code;
            }
        }

        throw new Http3ConnectionException(Http3ErrorCode.MessageError,
            "RFC 9114 §4.3.2: Missing required :status pseudo-header in response.");
    }

    /// <summary>
    /// Returns true if the header name is a content header that belongs on
    /// <see cref="HttpContent.Headers"/> rather than <see cref="HttpResponseMessage.Headers"/>.
    /// </summary>
    private static bool IsContentHeader(string name) =>
        string.Equals(name, "content-type", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "content-length", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "content-encoding", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "content-language", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "content-location", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "content-disposition", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "content-range", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "expires", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "last-modified", StringComparison.OrdinalIgnoreCase);
}
