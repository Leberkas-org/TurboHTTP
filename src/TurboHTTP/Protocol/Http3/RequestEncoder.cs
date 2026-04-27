using System.Buffers;
using TurboHTTP.Protocol.Http3.Qpack;
using TurboHTTP.Protocol.Semantics;

namespace TurboHTTP.Protocol.Http3;

/// <summary>
/// RFC 9114 §4.1 — Encodes HTTP request messages as HTTP/3 frame sequences.
/// Uses QPACK (RFC 9204) for header compression instead of HPACK.
///
/// Unlike HTTP/2, HTTP/3 frames have no stream identifier (QUIC provides that)
/// and no flags byte. Header blocks are never fragmented across CONTINUATION frames
/// (HTTP/3 has no CONTINUATION frame type).
///
/// Delegates QPACK encoding to the <see cref="QpackTableSync"/>-owned encoder.
/// One instance per connection.
/// </summary>
internal sealed class RequestEncoder
{
    // Tracks MemoryPool rentals from the previous Encode() call so they can be
    // disposed once the caller has consumed the frame list (contract: callers consume
    // frames before the next Encode() call).
    private readonly List<IMemoryOwner<byte>> _rentedOwners = new(4);
    private readonly List<Http3Frame> _reusableFrames = new(4);
    private readonly List<(string Name, string Value)> _reusableHeaders = new(16);
    private readonly QpackTableSync _tableSync;

    /// <summary>
    /// Creates a new HTTP/3 request encoder.
    /// </summary>
    /// <param name="tableSync">
    /// The QPACK table synchronization coordinator that owns the encoder.
    /// </param>
    public RequestEncoder(QpackTableSync tableSync)
    {
        ArgumentNullException.ThrowIfNull(tableSync);
        _tableSync = tableSync;
    }

    /// <summary>
    /// Encoder instructions emitted during the most recent <see cref="Encode"/> call.
    /// These must be sent on the QPACK encoder instruction stream (unidirectional stream
    /// type 0x02) before the HEADERS frame is transmitted on the request stream.
    /// </summary>
    public ReadOnlyMemory<byte> EncoderInstructions => _tableSync.Encoder.EncoderInstructions;

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

        // Dispose MemoryPool rentals from the previous Encode() call.
        // Safe: callers consume the frame list before calling Encode() again.
        ReturnRentedBuffers();

        // RFC 9114 §10.3: Validate origin before encoding
        OriginValidator.Validate(request.RequestUri, isConnect: request.Method == HttpMethod.Connect);

        _reusableHeaders.Clear();
        BuildHeaderList(request, _reusableHeaders);
        ValidatePseudoHeaders(_reusableHeaders);
        FieldValidator.Validate(_reusableHeaders);

        // QPACK encode directly into a MemoryPool-rented buffer
        var qpackOwner = MemoryPool<byte>.Shared.Rent(8192);
        _rentedOwners.Add(qpackOwner);
        var qpackSpan = qpackOwner.Memory.Span;
        var qpackBytesWritten = _tableSync.Encoder.Encode(_reusableHeaders, ref qpackSpan);
        var headerBlock = qpackOwner.Memory[..qpackBytesWritten];

        _reusableFrames.Clear();
        _reusableFrames.Add(new Http3HeadersFrame(headerBlock));

        // DATA frames carry the request body (if any)
        if (request.Content != null)
        {
            var contentStream = request.Content.ReadAsStream();
            var contentLength = request.Content.Headers.ContentLength;

            if (contentLength is > 0)
            {
                var size = (int)Math.Min(contentLength.Value, int.MaxValue);
                var bodyOwner = MemoryPool<byte>.Shared.Rent(size);
                var totalRead = 0;
                int bytesRead;

                while (totalRead < size &&
                       (bytesRead = contentStream.Read(bodyOwner.Memory.Span[totalRead..size])) > 0)
                {
                    totalRead += bytesRead;
                }

                if (totalRead > 0)
                {
                    _rentedOwners.Add(bodyOwner);
                    _reusableFrames.Add(new Http3DataFrame(bodyOwner.Memory[..totalRead]));
                }
                else
                {
                    bodyOwner.Dispose();
                }
            }
            else
            {
                const int chunkSize = 262_144;
                int bytesRead;

                while (true)
                {
                    var chunkOwner = MemoryPool<byte>.Shared.Rent(chunkSize);
                    var chunkFilled = 0;

                    while (chunkFilled < chunkSize &&
                           (bytesRead = contentStream.Read(chunkOwner.Memory.Span[chunkFilled..chunkSize])) > 0)
                    {
                        chunkFilled += bytesRead;
                    }

                    if (chunkFilled > 0)
                    {
                        _rentedOwners.Add(chunkOwner);
                        _reusableFrames.Add(new Http3DataFrame(chunkOwner.Memory[..chunkFilled]));
                    }
                    else
                    {
                        chunkOwner.Dispose();
                    }

                    if (chunkFilled < chunkSize)
                    {
                        break;
                    }
                }
            }
        }

        return _reusableFrames;
    }

    /// <summary>
    /// Convenience method that encodes a request and returns the raw QPACK header block.
    /// Used by tests to verify header encoding details.
    /// </summary>
    internal (IMemoryOwner<byte> Owner, int Length) EncodeToQpackBlock(HttpRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.RequestUri);

        OriginValidator.Validate(request.RequestUri,
            isConnect: request.Method == HttpMethod.Connect);

        _reusableHeaders.Clear();
        BuildHeaderList(request, _reusableHeaders);
        ValidatePseudoHeaders(_reusableHeaders);
        FieldValidator.Validate(_reusableHeaders);

        var owner = MemoryPool<byte>.Shared.Rent(8192);
        var span = owner.Memory.Span;
        var n = _tableSync.Encoder.Encode(_reusableHeaders, ref span);
        return (owner, n);
    }

    /// <summary>
    /// Disposes all MemoryPool rentals from the previous Encode() call.
    /// Must be called before reusing the frame list.
    /// </summary>
    private void ReturnRentedBuffers()
    {
        foreach (var owner in _rentedOwners)
        {
            owner.Dispose();
        }

        _rentedOwners.Clear();
    }

    /// <summary>
    /// Builds the ordered header list from an <see cref="HttpRequestMessage"/>.
    /// Pseudo-headers come first per RFC 9114 §4.3, followed by regular headers.
    /// Connection-specific headers are filtered out per RFC 9114 §4.2.
    ///
    /// For CONNECT requests (RFC 9114 §4.4), only :method and :authority are included.
    /// The :scheme and :path pseudo-headers MUST NOT be present.
    /// </summary>
    private static void BuildHeaderList(HttpRequestMessage request, List<(string Name, string Value)> headers)
    {
        var uri = request.RequestUri!;

        if (request.Method == HttpMethod.Connect)
        {
            headers.Add((":method", "CONNECT"));
            headers.Add((":authority", UriSanitizer.FormatAuthorityWithPort(uri)));
        }
        else
        {
            var pathAndQuery = string.IsNullOrEmpty(uri.Query)
                ? uri.AbsolutePath
                : string.Concat(uri.AbsolutePath, uri.Query);

            headers.Add((":method", request.Method.Method));
            headers.Add((":path", pathAndQuery));
            headers.Add((":scheme", uri.Scheme));
            headers.Add((":authority", UriSanitizer.FormatAuthority(uri)));
        }

        foreach (var h in request.Headers)
        {
            if (!IsForbidden(h.Key))
            {
                headers.Add((ToLower(h.Key), JoinValues(h.Value)));
            }
        }

        if (request.Content != null)
        {
            foreach (var h in request.Content.Headers)
            {
                headers.Add((ToLower(h.Key), JoinValues(h.Value)));
            }
        }
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
    /// by <see cref="FieldValidator"/> after construction.
    /// </summary>
    private static bool IsForbidden(string name) =>
        string.Equals(name, "connection", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "transfer-encoding", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "upgrade", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "proxy-connection", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "keep-alive", StringComparison.OrdinalIgnoreCase);

    private static string ToLower(string name)
    {
        foreach (var c in name)
        {
            if (c is >= 'A' and <= 'Z')
            {
                return name.ToLowerInvariant();
            }
        }

        return name;
    }

    private static string JoinValues(IEnumerable<string> values)
    {
        string? first = null;
        foreach (var v in values)
        {
            if (first is null)
            {
                first = v;
                continue;
            }

            return string.Join(", ", values);
        }

        return first ?? string.Empty;
    }
}