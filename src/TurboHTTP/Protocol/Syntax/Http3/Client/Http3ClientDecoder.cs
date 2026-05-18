using System.Net;
using TurboHTTP.Protocol.Semantics;
using TurboHTTP.Protocol.Syntax.Http3.Qpack;

namespace TurboHTTP.Protocol.Syntax.Http3.Client;

internal sealed class Http3ClientDecoder
{
    private static readonly HttpContent SharedEmptyContent = new ByteArrayContent([]);

    private readonly QpackTableSync _tableSync;
    private readonly int _maxFieldSectionSize;

    public Http3ClientDecoder(QpackTableSync tableSync, int maxFieldSectionSize = int.MaxValue)
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
    /// false if this was a trailing HEADERS (trailers are stored in response).
    /// </summary>
    public bool DecodeHeaders(HeadersFrame frame, StreamState state)
    {
        if (state.HasResponse)
        {
            var trailerHeaders = _tableSync.Decoder.Decode(frame.HeaderBlock.Span);
            ApplyTrailers(trailerHeaders, state);
            return false;
        }

        var headers = _tableSync.Decoder.Decode(frame.HeaderBlock.Span);
        return AssembleHeaders(headers, state);
    }

    /// <summary>
    /// Assembles response headers from pre-decoded QPACK header fields.
    /// Used when headers are decoded via <see cref="Qpack.QpackTableSync.TryDecodeOrBlock"/>
    /// for proper RFC 9204 §2.1.2 blocked stream handling.
    /// Trailers are stored in response when a response already exists.
    /// </summary>
    public bool AssembleHeaders(IReadOnlyList<(string Name, string Value)> headers, StreamState state)
    {
        if (state.HasResponse)
        {
            ApplyTrailers(headers, state);
            return false;
        }

        FieldValidator.ValidateResponsePseudoHeaders(headers);
        FieldValidator.Validate(headers);
        ValidateFieldSectionSize(headers);

        var response = state.InitResponse();

        foreach (var h in headers)
        {
            if (h.Name == WellKnownHeaders.Status)
            {
                response.StatusCode = (HttpStatusCode)int.Parse(h.Value);
            }
            else if (!h.Name.StartsWith(':'))
            {
                response.Headers.TryAddWithoutValidation(h.Name, h.Value);

                if (string.Equals(h.Name, WellKnownHeaders.ContentLength, StringComparison.OrdinalIgnoreCase) &&
                    long.TryParse(h.Value, out var cl))
                {
                    state.ExpectedContentLength = cl;
                }

                if (ContentHeaderClassifier.IsContentHeader(h.Name))
                {
                    state.AddContentHeader(h.Name, h.Value);
                }
            }
        }

        return true;
    }

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
            throw new HttpProtocolException(
                "RFC 9114 §4.2.2: Received field section exceeds SETTINGS_MAX_FIELD_SECTION_SIZE");
        }
    }

    private static void ApplyTrailers(IReadOnlyList<(string Name, string Value)> headers, StreamState state)
    {
        var response = state.GetResponse();
        foreach (var (name, value) in headers)
        {
            if (name.StartsWith(':'))
            {
                throw new HttpProtocolException(
                    "RFC 9114 §4.3: Pseudo-header fields MUST NOT appear in trailer sections");
            }

            if (TrailerFieldValidator.IsAllowedInTrailer(name))
            {
                response.TrailingHeaders.TryAddWithoutValidation(name, value);
            }
        }
    }
}