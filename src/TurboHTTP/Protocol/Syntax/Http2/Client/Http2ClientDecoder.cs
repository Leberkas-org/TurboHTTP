using System.Net;
using TurboHTTP.Protocol.Semantics;
using TurboHTTP.Protocol.Syntax.Http2.Hpack;

namespace TurboHTTP.Protocol.Syntax.Http2.Client;

internal sealed class Http2ClientDecoder(
    int maxHeaderSize = 16 * 1024,
    int maxTotalHeaderSize = 64 * 1024)
{
    private const string PseudoHeaderSection = "RFC 9113 §8.1.2.2";
    private const string UppercaseSection = "RFC 9113 §8.2.1";
    private const string TokenSection = "RFC 9113 §10.3";
    private const string FieldValueSection = "RFC 9113 §10.3";
    private const string ConnectionSection = "RFC 9113 §8.2.2";

    private static readonly HttpContent SharedEmptyContent = new ByteArrayContent([]);

    private HpackDecoder _hpack = new();

    public void ResetHpack()
    {
        _hpack = new HpackDecoder();
    }

    public HttpResponseMessage? DecodeHeaders(int streamId, bool endStream, StreamState state)
    {
        var headers = _hpack.Decode(state.GetHeaderSpan());
        ValidateHeaderSize(headers, streamId);
        ValidateResponseHeaders(headers);

        var response = new HttpResponseMessage();
        AssembleResponse(headers, response, state);

        state.InitResponse(response);

        if (!endStream)
        {
            return null;
        }

        response.Content = state.HasContentHeaders
            ? new ByteArrayContent([])
            : SharedEmptyContent;
        state.ApplyContentHeadersTo(response.Content);

        return response;
    }

    public HttpResponseMessage DecodeHeadersForStreaming(int streamId, StreamState state)
    {
        var headers = _hpack.Decode(state.GetHeaderSpan());
        ValidateHeaderSize(headers, streamId);
        ValidateResponseHeaders(headers);

        var response = new HttpResponseMessage();
        AssembleResponse(headers, response, state);

        state.InitResponse(response);
        return response;
    }

    internal static void ValidateResponseHeaders(List<HpackHeader> headers)
    {
        PseudoHeaderValidator.ValidateResponsePseudoHeaders(
            headers,
            static h => h.Name,
            PseudoHeaderSection);

        FieldValidator.Validate(
            headers,
            static h => h.Name,
            static h => h.Value,
            UppercaseSection,
            TokenSection,
            FieldValueSection,
            ConnectionSection);
    }

    private void ValidateHeaderSize(List<HpackHeader> headers, int streamId)
    {
        var totalHeaderSize = 0;

        for (var i = 0; i < headers.Count; i++)
        {
            var headerSize = headers[i].Name.Length + headers[i].Value.Length;

            if (headerSize > maxHeaderSize)
            {
                throw new HttpProtocolException(
                    $"RFC 9113 §10.5.1: Single header field size {headerSize} bytes " +
                    $"exceeds MaxHeaderSize limit ({maxHeaderSize} bytes) " +
                    $"on stream {streamId} — header '{headers[i].Name}'.");
            }

            totalHeaderSize += headerSize;

            if (totalHeaderSize > maxTotalHeaderSize)
            {
                throw new HttpProtocolException(
                    $"RFC 9113 §10.5.1: Total header block size {totalHeaderSize} bytes " +
                    $"exceeds MaxTotalHeaderSize limit ({maxTotalHeaderSize} bytes) " +
                    $"on stream {streamId}.");
            }
        }
    }

    private static void AssembleResponse(List<HpackHeader> headers, HttpResponseMessage response, StreamState state)
    {
        foreach (var h in headers)
        {
            if (h.Name == WellKnownHeaders.Status)
            {
                response.StatusCode = (HttpStatusCode)int.Parse(h.Value);
            }
            else if (!h.Name.StartsWith(WellKnownHeaders.Colon))
            {
                response.Headers.TryAddWithoutValidation(h.Name, h.Value);

                if (ContentHeaderClassifier.IsContentHeader(h.Name))
                {
                    state.AddContentHeader(h.Name, h.Value);
                }
            }
        }
    }
}
