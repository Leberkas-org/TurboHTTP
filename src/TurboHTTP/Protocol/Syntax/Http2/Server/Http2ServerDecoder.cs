using Microsoft.AspNetCore.Http;
using TurboHTTP.Protocol.Semantics;
using TurboHTTP.Protocol.Syntax.Http2.Hpack;
using TurboHTTP.Server.Context.Features;

namespace TurboHTTP.Protocol.Syntax.Http2.Server;

internal sealed class Http2ServerDecoder
{
    private const string PseudoHeaderSection = "RFC 9113 §8.3.1";
    private const string UppercaseSection = "RFC 9113 §8.2.1";
    private const string TokenSection = "RFC 9113 §10.3";
    private const string FieldValueSection = "RFC 9113 §10.3";
    private const string ConnectionSection = "RFC 9113 §8.2.2";

    private HpackDecoder _hpack = new();
    private readonly int _maxHeaderSize;
    private readonly int _maxTotalHeaderSize;

    public Http2ServerDecoder(int maxHeaderSize = 16 * 1024, int maxTotalHeaderSize = 64 * 1024)
    {
        _maxHeaderSize = maxHeaderSize;
        _maxTotalHeaderSize = maxTotalHeaderSize;
    }

    public void ResetHpack()
    {
        _hpack = new HpackDecoder();
    }

    public TurboHttpRequestFeature? DecodeHeadersToFeature(int streamId, bool endStream, StreamState state)
    {
        var headers = _hpack.Decode(state.GetHeaderSpan());
        ValidateHeaderSize(headers, streamId);
        ValidateRequestHeaders(headers);

        var feature = new TurboHttpRequestFeature { Protocol = "HTTP/2" };
        var headerDict = new HeaderDictionary();

        string? path = null;
        string? scheme = null;
        string? authority = null;
        var isConnect = false;

        foreach (var h in headers)
        {
            if (h.Name == WellKnownHeaders.Method)
            {
                feature.Method = h.Value;
                if (h.Value == WellKnownHeaders.Connect)
                {
                    isConnect = true;
                }
            }
            else if (h.Name == WellKnownHeaders.Path)
            {
                path = h.Value;
                state.AddPseudoHeader(WellKnownHeaders.Path, h.Value);
            }
            else if (h.Name == WellKnownHeaders.Scheme)
            {
                scheme = h.Value;
                state.AddPseudoHeader(WellKnownHeaders.Scheme, h.Value);
            }
            else if (h.Name == WellKnownHeaders.Authority)
            {
                authority = h.Value;
                state.AddPseudoHeader(WellKnownHeaders.Authority, h.Value);
            }
            else if (!h.Name.StartsWith(WellKnownHeaders.Colon))
            {
                if (headerDict.TryGetValue(h.Name, out var existing))
                {
                    headerDict[h.Name] = Microsoft.Extensions.Primitives.StringValues.Concat(existing, h.Value);
                }
                else
                {
                    headerDict[h.Name] = h.Value;
                }

                if (ContentHeaderClassifier.IsContentHeader(h.Name))
                {
                    state.AddContentHeader(h.Name, h.Value);
                }
            }
        }

        if (!isConnect && path is not null)
        {
            feature.Scheme = scheme ?? "https";
            feature.RawTarget = path;

            var queryIdx = path.IndexOf('?');
            feature.Path = queryIdx >= 0 ? path[..queryIdx] : path;
            feature.QueryString = queryIdx >= 0 ? path[queryIdx..] : string.Empty;
        }

        feature.Headers = headerDict;
        state.InitRequestFeature(feature);

        if (!endStream)
        {
            return null;
        }

        return feature;
    }

    private static void ValidateRequestHeaders(List<HpackHeader> headers)
    {
        PseudoHeaderValidator.ValidateRequestPseudoHeaders(
            headers,
            static h => h.Name,
            static h => h.Value,
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

            if (headerSize > _maxHeaderSize)
            {
                throw new HttpProtocolException(
                    $"RFC 9113 §10.5.1: Single header field size {headerSize} bytes " +
                    $"exceeds MaxHeaderSize limit ({_maxHeaderSize} bytes) " +
                    $"on stream {streamId} — header '{headers[i].Name}'.");
            }

            totalHeaderSize += headerSize;

            if (totalHeaderSize > _maxTotalHeaderSize)
            {
                throw new HttpProtocolException(
                    $"RFC 9113 §10.5.1: Total header block size {totalHeaderSize} bytes " +
                    $"exceeds MaxTotalHeaderSize limit ({_maxTotalHeaderSize} bytes) " +
                    $"on stream {streamId}.");
            }
        }
    }
}
