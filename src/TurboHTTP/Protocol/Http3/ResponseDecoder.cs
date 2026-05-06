using System.Net;
using TurboHTTP.Internal;
using TurboHTTP.Protocol.Http3.Qpack;

namespace TurboHTTP.Protocol.Http3;

/// <summary>
/// Decodes HTTP/3 response headers (RFC 9114 §4.1) and assembles
/// <see cref="HttpResponseMessage"/> from per-stream state.
/// Extracted from <see cref="StateMachine"/> for independent testability.
/// Mirrors the HTTP/2 <see cref="Http2.ResponseDecoder"/> pattern.
/// </summary>
internal sealed class ResponseDecoder
{
    private static readonly HttpContent SharedEmptyContent = new ByteArrayContent([]);

    private readonly QpackTableSync _tableSync;
    private readonly int _maxFieldSectionSize;

    public ResponseDecoder(QpackTableSync tableSync, int maxFieldSectionSize = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(tableSync);
        _tableSync = tableSync;
        _maxFieldSectionSize = maxFieldSectionSize;
    }

    /// <summary>
    /// Decoder instructions (Section Acknowledgments) emitted during the most recent decode.
    /// Must be sent on the decoder instruction stream (RFC 9204 §4.4.1).
    /// </summary>
    public ReadOnlyMemory<byte> DecoderInstructions => _tableSync.Decoder.DecoderInstructions;

    /// <summary>
    /// Decode a HEADERS frame into response headers on the given stream state.
    /// Returns true if a new response was created (first HEADERS),
    /// false if this was a trailing HEADERS (trailers not yet supported).
    /// </summary>
    public bool DecodeHeaders(HeadersFrame frame, StreamState state)
    {
        if (state.HasResponse)
        {
            return false;
        }

        var headers = _tableSync.Decoder.Decode(frame.HeaderBlock.Span);
        return AssembleHeaders(headers, state);
    }

    /// <summary>
    /// Assembles response headers from pre-decoded QPACK header fields.
    /// Used when headers are decoded via <see cref="Qpack.QpackTableSync.TryDecodeOrBlock"/>
    /// for proper RFC 9204 §2.1.2 blocked stream handling.
    /// </summary>
    public bool AssembleHeaders(IReadOnlyList<(string Name, string Value)> headers, StreamState state)
    {
        if (state.HasResponse)
        {
            return false;
        }

        FieldValidator.ValidateResponsePseudoHeaders(headers);
        FieldValidator.Validate(headers);
        ValidateFieldSectionSize(headers);

        var response = state.InitResponse();

        foreach (var h in headers)
        {
            if (h.Name == ":status")
            {
                response.StatusCode = (HttpStatusCode)int.Parse(h.Value);
            }
            else if (!h.Name.StartsWith(':'))
            {
                response.Headers.TryAddWithoutValidation(h.Name, h.Value);

                if (string.Equals(h.Name, "content-length", StringComparison.OrdinalIgnoreCase) &&
                    long.TryParse(h.Value, out var cl))
                {
                    state.ExpectedContentLength = cl;
                }

                if (IsContentHeader(h.Name))
                {
                    state.AddContentHeader(h.Name, h.Value);
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Accumulate DATA frame payload into stream body buffer.
    /// Returns false if no response headers have been received yet (protocol violation).
    /// </summary>
    public bool AccumulateData(DataFrame frame, StreamState state)
    {
        if (!state.HasResponse)
        {
            return false;
        }

        var data = frame.Data.Span;
        if (data.Length > 0)
        {
            state.AppendBody(data);
        }

        return true;
    }

    /// <summary>
    /// Build the final <see cref="HttpResponseMessage"/> from accumulated stream state.
    /// Attaches body content and applies deferred content headers.
    /// </summary>
    public HttpResponseMessage CompleteResponse(StreamState state)
    {
        if (state.ExpectedContentLength.HasValue &&
            state.AccumulatedBodyLength != state.ExpectedContentLength.Value)
        {
            throw new Http3Exception(ErrorCode.MessageError,
                string.Concat("RFC 9114 §4.1.2: Content-Length mismatch — expected ",
                    state.ExpectedContentLength.Value.ToString(), ", received ",
                    state.AccumulatedBodyLength.ToString()));
        }

        var response = state.GetResponse();
        var (bodyOwner, bodyLength) = state.TakeBodyOwnership();

        if (bodyLength > 0 && bodyOwner is not null)
        {
            response.Content = new PooledBodyContent(bodyOwner, bodyLength);
        }
        else
        {
            bodyOwner?.Dispose();
            response.Content = state.HasContentHeaders
                ? new ByteArrayContent([])
                : SharedEmptyContent;
        }

        state.ApplyContentHeadersTo(response.Content);
        return response;
    }

    public static bool IsContentHeader(string name) =>
        name.StartsWith("content-", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("allow", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("expires", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("last-modified", StringComparison.OrdinalIgnoreCase);

    private void ValidateFieldSectionSize(IReadOnlyList<(string Name, string Value)> headers)
    {
        if (_maxFieldSectionSize == int.MaxValue)
        {
            return;
        }

        var totalSize = 0L;
        foreach (var (name, value) in headers)
        {
            totalSize += name.Length + value.Length + 32;
        }

        if (totalSize > _maxFieldSectionSize)
        {
            throw new Http3Exception(ErrorCode.ExcessiveLoad,
                "RFC 9114 §4.2.2: Received field section exceeds SETTINGS_MAX_FIELD_SECTION_SIZE");
        }
    }
}
