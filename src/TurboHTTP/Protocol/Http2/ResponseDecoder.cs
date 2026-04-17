using System.Net;
using TurboHTTP.Internal;
using TurboHTTP.Protocol.Http2.Hpack;

namespace TurboHTTP.Protocol.Http2;

/// <summary>
/// Decodes HTTP/2 response headers (RFC 9113 §6.2, §10.5.1) and assembles
/// <see cref="HttpResponseMessage"/> from stream state.
/// Extracted from Http20ConnectionStage.Logic for independent testability.
/// </summary>
internal sealed class ResponseDecoder
{
    // Shared empty content — reused for headers-only responses with no content headers.
    private static readonly HttpContent SharedEmptyContent = new ByteArrayContent([]);

    private HpackDecoder _hpack;
    private readonly int _maxHeaderSize;
    private readonly int _maxTotalHeaderSize;

    public ResponseDecoder(HpackDecoder hpack, int maxHeaderSize = 16 * 1024, int maxTotalHeaderSize = 64 * 1024)
    {
        _hpack = hpack;
        _maxHeaderSize = maxHeaderSize;
        _maxTotalHeaderSize = maxTotalHeaderSize;
    }

    /// <summary>
    /// Resets HPACK decoder state for reconnect.
    /// Must be called before decoding responses on a new connection.
    /// </summary>
    public void ResetHpack()
    {
        _hpack = new HpackDecoder();
    }

    /// <summary>
    /// Decode header block from stream state and build an HttpResponseMessage.
    /// If <paramref name="endStream"/> is true, returns the completed response (headers-only).
    /// Otherwise, stores the partial response on the state and returns null.
    /// </summary>
    public HttpResponseMessage? DecodeHeaders(int streamId, bool endStream, StreamState state)
    {
        var headers = _hpack.Decode(state.GetHeaderSpan());
        var totalHeaderSize = 0;

        var response = new HttpResponseMessage();
        var hasStatus = false;

        foreach (var h in headers)
        {
            var headerSize = h.Name.Length + h.Value.Length;

            if (headerSize > _maxHeaderSize)
            {
                throw new Http2Exception(
                    $"RFC 9113 §10.5.1: Single header field size {headerSize} bytes " +
                    $"exceeds MaxHeaderSize limit ({_maxHeaderSize} bytes) " +
                    $"on stream {streamId} — header '{h.Name}'.",
                    Http2ErrorCode.FrameSizeError,
                    Http2ErrorScope.Stream,
                    streamId);
            }

            totalHeaderSize += headerSize;

            if (totalHeaderSize > _maxTotalHeaderSize)
            {
                throw new Http2Exception(
                    $"RFC 9113 §10.5.1: Total header block size {totalHeaderSize} bytes " +
                    $"exceeds MaxTotalHeaderSize limit ({_maxTotalHeaderSize} bytes) " +
                    $"on stream {streamId}.",
                    Http2ErrorCode.FrameSizeError,
                    Http2ErrorScope.Stream,
                    streamId);
            }

            if (h.Name == ":status")
            {
                response.StatusCode = (HttpStatusCode)int.Parse(h.Value);
                hasStatus = true;
            }
            else if (!h.Name.StartsWith(':'))
            {
                response.Headers.TryAddWithoutValidation(h.Name, h.Value);

                if (IsContentHeader(h.Name))
                {
                    state.AddContentHeader(h.Name, h.Value);
                }
            }
        }

        if (!hasStatus)
        {
            throw new FormatException($"RFC 9113 §6.3.1: Missing required :status pseudo-header on stream {streamId}.");
        }

        state.InitResponse(response);

        if (!endStream)
        {
            return null;
        }

        // Headers-only response (no body).
        response.Content = state.HasContentHeaders
            ? new ByteArrayContent([])
            : SharedEmptyContent;
        state.ApplyContentHeadersTo(response.Content);

        return response;
    }

    /// <summary>
    /// Build response from accumulated body data (called on DATA EndStream).
    /// </summary>
    public HttpResponseMessage CompleteDataResponse(StreamState state)
    {
        var response = state.GetOrCreateResponse();

        var (bodyOwner, bodyLength) = state.TakeBodyOwnership();
        response.Content = bodyOwner is null
            ? (state.HasContentHeaders ? new ByteArrayContent([]) : SharedEmptyContent)
            : new PooledBodyContent(bodyOwner, bodyLength);
        state.ApplyContentHeadersTo(response.Content);

        return response;
    }

    public static bool IsContentHeader(string name) =>
        name.StartsWith("content-", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("allow", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("expires", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("last-modified", StringComparison.OrdinalIgnoreCase);
}
