using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using TurboHttp.Protocol.RFC9110;
using TurboHttp.Protocol.RFC9204;

namespace TurboHttp.Protocol.RFC9114;

/// <summary>
/// RFC 9114 §4.1 — Encodes HTTP request messages as HTTP/3 frame sequences.
/// Uses QPACK (RFC 9204) for header compression instead of HPACK.
///
/// Unlike HTTP/2, HTTP/3 frames have no stream identifier (QUIC provides that)
/// and no flags byte. Header blocks are never fragmented across CONTINUATION frames
/// (HTTP/3 has no CONTINUATION frame type).
///
/// Stateful: maintains a <see cref="QpackEncoder"/> instance for header compression.
/// One instance per connection.
/// </summary>
public sealed class Http3RequestEncoder
{
    /// <summary>
    /// Creates a new HTTP/3 request encoder.
    /// </summary>
    /// <param name="maxTableCapacity">
    /// Maximum QPACK dynamic table capacity in bytes (SETTINGS_QPACK_MAX_TABLE_CAPACITY).
    /// Set to 0 to disable dynamic table usage.
    /// </param>
    public Http3RequestEncoder(int maxTableCapacity = 4096)
    {
        QpackEncoder = new QpackEncoder(maxTableCapacity);
    }

    /// <summary>
    /// The underlying QPACK encoder (for inspection and testing).
    /// </summary>
    public QpackEncoder QpackEncoder { get; }

    /// <summary>
    /// Encoder instructions emitted during the most recent <see cref="Encode"/> call.
    /// These must be sent on the QPACK encoder instruction stream (unidirectional stream
    /// type 0x02) before the HEADERS frame is transmitted on the request stream.
    /// </summary>
    public ReadOnlyMemory<byte> EncoderInstructions => QpackEncoder.EncoderInstructions;

    /// <summary>
    /// Encodes an HTTP request message into a list of HTTP/3 frames.
    ///
    /// The result contains:
    /// - A HEADERS frame with the QPACK-compressed header block
    /// - Zero or more DATA frames if the request has a body
    ///
    /// After calling this method, check <see cref="EncoderInstructions"/> for any
    /// QPACK encoder instructions that must be sent on the encoder stream.
    /// </summary>
    /// <param name="request">The HTTP request message to encode.</param>
    /// <returns>The list of HTTP/3 frames representing the request.</returns>
    public IReadOnlyList<Http3Frame> Encode(HttpRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.RequestUri);

        // RFC 9114 §10.3: Validate origin before encoding
        Http3OriginValidator.Validate(request.RequestUri,
            isConnect: request.Method == HttpMethod.Connect);

        var headers = BuildHeaderList(request);
        ValidatePseudoHeaders(headers);
        Http3FieldValidator.Validate(headers);

        var headerBlock = QpackEncoder.Encode(headers);
        var frames = new List<Http3Frame>();

        // HEADERS frame carries the compressed header block
        frames.Add(new Http3HeadersFrame(headerBlock));

        // DATA frames carry the request body (if any)
        if (request.Content != null)
        {
            using var ms = new MemoryStream();
            request.Content.CopyTo(ms, null, new CancellationToken(false));
            var body = ms.ToArray();

            if (body.Length > 0)
            {
                frames.Add(new Http3DataFrame(body));
            }
        }

        return frames;
    }

    /// <summary>
    /// Convenience method that encodes a request and returns the raw QPACK header block.
    /// Used by tests to verify header encoding details.
    /// </summary>
    internal ReadOnlyMemory<byte> EncodeToQpackBlock(HttpRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.RequestUri);

        Http3OriginValidator.Validate(request.RequestUri,
            isConnect: request.Method == HttpMethod.Connect);

        var headers = BuildHeaderList(request);
        ValidatePseudoHeaders(headers);
        Http3FieldValidator.Validate(headers);
        return QpackEncoder.Encode(headers);
    }

    /// <summary>
    /// Builds the ordered header list from an <see cref="HttpRequestMessage"/>.
    /// Pseudo-headers come first per RFC 9114 §4.3, followed by regular headers.
    /// Connection-specific headers are filtered out per RFC 9114 §4.2.
    ///
    /// For CONNECT requests (RFC 9114 §4.4), only :method and :authority are included.
    /// The :scheme and :path pseudo-headers MUST NOT be present.
    /// </summary>
    private static List<(string Name, string Value)> BuildHeaderList(HttpRequestMessage request)
    {
        var uri = request.RequestUri!;

        List<(string Name, string Value)> headers;

        if (request.Method == HttpMethod.Connect)
        {
            // RFC 9114 §4.4: CONNECT uses only :method and :authority
            headers =
            [
                (":method", "CONNECT"),
                (":authority", UriSanitizer.FormatAuthorityWithPort(uri)),
            ];
        }
        else
        {
            var pathAndQuery = string.IsNullOrEmpty(uri.Query)
                ? uri.AbsolutePath
                : uri.AbsolutePath + uri.Query;

            headers =
            [
                (":method", request.Method.Method),
                (":path", pathAndQuery),
                (":scheme", uri.Scheme),
                (":authority", UriSanitizer.FormatAuthority(uri)),
            ];
        }

        // Regular headers (excluding connection-specific headers)
        headers.AddRange(request.Headers
            .Where(x => !IsForbidden(x.Key))
            .Select(h => (h.Key.ToLowerInvariant(), string.Join(", ", h.Value))));

        if (request.Content != null)
        {
            headers.AddRange(request.Content.Headers
                .Select(h => (h.Key.ToLowerInvariant(), string.Join(", ", h.Value))));
        }

        return headers;
    }

    /// <summary>
    /// Validates pseudo-headers per RFC 9114 §4.3.1 and §4.4:
    /// - Normal requests: all four required (:method, :path, :scheme, :authority)
    /// - CONNECT requests: only :method and :authority (:scheme and :path MUST NOT be present)
    /// - Must appear before regular headers
    /// - Must have exactly one of each (no duplicates)
    /// - No unknown pseudo-headers allowed
    /// </summary>
    internal static void ValidatePseudoHeaders(List<(string Name, string Value)> headers)
    {
        var hasMethod = false;
        var hasPath = false;
        var hasScheme = false;
        var hasAuthority = false;
        var lastPseudoIndex = -1;
        var firstRegularIndex = int.MaxValue;
        string? methodValue = null;

        for (var i = 0; i < headers.Count; i++)
        {
            var (name, value) = headers[i];

            if (name.StartsWith(':'))
            {
                lastPseudoIndex = i;

                switch (name)
                {
                    case ":method":
                        if (hasMethod)
                        {
                            throw new Http3Exception(Http3ErrorCode.MessageError,
                                "RFC 9114 §4.3.1: Duplicate :method pseudo-header");
                        }
                        hasMethod = true;
                        methodValue = value;
                        break;
                    case ":path":
                        if (hasPath)
                        {
                            throw new Http3Exception(Http3ErrorCode.MessageError,
                                "RFC 9114 §4.3.1: Duplicate :path pseudo-header");
                        }
                        hasPath = true;
                        break;
                    case ":scheme":
                        if (hasScheme)
                        {
                            throw new Http3Exception(Http3ErrorCode.MessageError,
                                "RFC 9114 §4.3.1: Duplicate :scheme pseudo-header");
                        }
                        hasScheme = true;
                        break;
                    case ":authority":
                        if (hasAuthority)
                        {
                            throw new Http3Exception(Http3ErrorCode.MessageError,
                                "RFC 9114 §4.3.1: Duplicate :authority pseudo-header");
                        }
                        hasAuthority = true;
                        break;
                    default:
                        throw new Http3Exception(Http3ErrorCode.MessageError,
                            $"RFC 9114 §4.3.1: Unknown request pseudo-header '{name}'");
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
            throw new Http3Exception(Http3ErrorCode.MessageError,
                $"RFC 9114 §4.3.1: Pseudo-header at index {lastPseudoIndex} appears after regular header at index {firstRegularIndex}");
        }

        // RFC 9114 §4.4: CONNECT requests MUST NOT include :scheme or :path
        if (string.Equals(methodValue, "CONNECT", StringComparison.Ordinal))
        {
            if (hasScheme)
            {
                throw new Http3Exception(Http3ErrorCode.MessageError,
                    "RFC 9114 §4.4: CONNECT request MUST NOT include :scheme pseudo-header");
            }

            if (hasPath)
            {
                throw new Http3Exception(Http3ErrorCode.MessageError,
                    "RFC 9114 §4.4: CONNECT request MUST NOT include :path pseudo-header");
            }

            if (!hasAuthority)
            {
                throw new Http3Exception(Http3ErrorCode.MessageError,
                    "RFC 9114 §4.4: CONNECT request MUST include :authority pseudo-header");
            }

            return;
        }

        var missing = new System.Text.StringBuilder();
        if (!hasMethod) missing.Append(":method");
        if (!hasPath) missing.Append(missing.Length > 0 ? ", :path" : ":path");
        if (!hasScheme) missing.Append(missing.Length > 0 ? ", :scheme" : ":scheme");
        if (!hasAuthority) missing.Append(missing.Length > 0 ? ", :authority" : ":authority");

        if (missing.Length > 0)
        {
            throw new Http3Exception(Http3ErrorCode.MessageError,
                $"RFC 9114 §4.3.1: Missing required pseudo-headers: {missing}");
        }
    }

    /// <summary>
    /// Connection-specific headers silently stripped during header list construction per RFC 9114 §4.2.
    /// Note: TE is NOT stripped here — it is allowed with value "trailers" and validated
    /// by <see cref="Http3FieldValidator"/> after construction.
    /// </summary>
    private static bool IsForbidden(string name) =>
        string.Equals(name, "connection", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "transfer-encoding", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "upgrade", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "proxy-connection", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "keep-alive", StringComparison.OrdinalIgnoreCase);
}
