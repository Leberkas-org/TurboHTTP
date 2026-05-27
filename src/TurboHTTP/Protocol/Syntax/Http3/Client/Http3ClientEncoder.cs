using System.Buffers;
using TurboHTTP.Protocol.Syntax.Http3.Qpack;
using TurboHTTP.Protocol.Semantics;

namespace TurboHTTP.Protocol.Syntax.Http3.Client;

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
internal sealed class Http3ClientEncoder
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
    public Http3ClientEncoder(QpackTableSync tableSync)
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
        var qpackOwner = MemoryPool<byte>.Shared.Rent(4 * 1024);
        _rentedOwners.Add(qpackOwner);
        var qpackWriter = SpanWriter.Create(qpackOwner.Memory.Span);
        var qpackBytesWritten = _tableSync.Encoder.Encode(_reusableHeaders, ref qpackWriter);
        var headerBlock = qpackOwner.Memory[..qpackBytesWritten];

        var peerLimit = _tableSync.RemoteMaxFieldSectionSize;
        if (qpackBytesWritten > peerLimit)
        {
            throw new HttpProtocolException(
                string.Concat("RFC 9114 §4.2.2: Encoded header block (", qpackBytesWritten.ToString(),
                    " bytes) exceeds peer SETTINGS_MAX_FIELD_SECTION_SIZE (", peerLimit.Value.ToString(), ")"));
        }

        _reusableFrames.Clear();
        _reusableFrames.Add(new HeadersFrame(headerBlock));

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

        OriginValidator.Validate(request.RequestUri, isConnect: request.Method == HttpMethod.Connect);

        _reusableHeaders.Clear();
        BuildHeaderList(request, _reusableHeaders);
        ValidatePseudoHeaders(_reusableHeaders);
        FieldValidator.Validate(_reusableHeaders);

        var owner = MemoryPool<byte>.Shared.Rent(4 * 1024);
        var w = SpanWriter.Create(owner.Memory.Span);
        var n = _tableSync.Encoder.Encode(_reusableHeaders, ref w);
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
            headers.Add((WellKnownHeaders.Method, WellKnownHeaders.Connect));
            headers.Add((WellKnownHeaders.Authority, UriSanitizer.FormatAuthorityWithPort(uri)));
        }
        else
        {
            var pathAndQuery = string.IsNullOrEmpty(uri.Query)
                ? uri.AbsolutePath
                : string.Concat(uri.AbsolutePath, uri.Query);

            headers.Add((WellKnownHeaders.Method, request.Method.Method));
            headers.Add((WellKnownHeaders.Path, pathAndQuery));
            headers.Add((WellKnownHeaders.Scheme, uri.Scheme));
            headers.Add((WellKnownHeaders.Authority, UriSanitizer.FormatAuthority(uri)));
        }

        foreach (var h in request.Headers)
        {
            if (!ContentHeaderClassifier.IsForbiddenConnectionHeaderExcludingTe(h.Key))
            {
                headers.Add((ContentHeaderClassifier.ToLowerAscii(h.Key), ContentHeaderClassifier.JoinHeaderValues(h.Value)));
            }
        }

        if (request.Content != null)
        {
            foreach (var h in request.Content.Headers)
            {
                headers.Add((ContentHeaderClassifier.ToLowerAscii(h.Key), ContentHeaderClassifier.JoinHeaderValues(h.Value)));
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
        => PseudoHeaderValidator.ValidateRequestPseudoHeaders(
            headers,
            static h => h.Name,
            static h => h.Value,
            "RFC 9114 §4.3.1");

}