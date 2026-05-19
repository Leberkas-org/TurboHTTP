using System.Net;
using TurboHTTP.Protocol.Semantics;
using TurboHTTP.Protocol.Syntax;
using TurboHTTP.Protocol.Syntax.Http11.Client;
using TurboHTTP.Protocol.Syntax.Http11.Options;

namespace TurboHTTP.Protocol;

internal static class HttpMessageSize
{
    private static readonly Http11ClientEncoderOptions DefaultOptions = new();

    public static int Estimate(HttpRequestMessage request, int bodyLength)
    {
        if (request.Version == HttpVersion.Version20)
        {
            return Http2Request(request, bodyLength);
        }

        if (request.Version == HttpVersion.Version30)
        {
            return Http3Request(request, bodyLength);
        }

        return Http1XRequest(request, bodyLength);
    }

    public static int Estimate(HttpResponseMessage response, int bodyLength)
    {
        if (response.Version == HttpVersion.Version20)
        {
            return Http2Response(response, bodyLength);
        }

        if (response.Version == HttpVersion.Version30)
        {
            return Http3Response(response, bodyLength);
        }

        return Http1XResponse(response, bodyLength);
    }

    // RFC 1945 §4 / RFC 9112 §3: method SP request-target SP HTTP-version CRLF + headers + CRLF + body
    private static int Http1XRequest(HttpRequestMessage request, int bodyLength)
    {
        var targetLength = request.ResolveTarget().Length;
        var versionLength = MessageVersionCodec.ToWireFormat(request.Version).Length;
        var requestLine = request.Method.Method.Length + 1 + targetLength + 1 + versionLength + 2;
        return requestLine + HeaderBuilder.Build(request, DefaultOptions).WireSize() + bodyLength;
    }

    // RFC 1945 §6.1 / RFC 9112 §4: HTTP-version SP status-code SP reason-phrase CRLF + headers + CRLF + body
    private static int Http1XResponse(HttpResponseMessage response, int bodyLength)
    {
        var versionLength = MessageVersionCodec.ToWireFormat(response.Version).Length;
        var statusLine = versionLength + 1 + 3 + 1 + (response.ReasonPhrase?.Length ?? 0) + 2;
        return statusLine + ResponseHeadersWireSize(response) + bodyLength;
    }

    // RFC 9113 §4.1: HEADERS frame (9-byte header + HPACK block) [+ DATA frame (9-byte header + body)]
    private static int Http2Request(HttpRequestMessage request, int bodyLength)
    {
        const int frameHeader = 9;
        var hpack = HpackLiteralSize(WellKnownHeaders.Method, request.Method.Method)
                    + HpackLiteralSize(WellKnownHeaders.Path, request.ResolveTarget())
                    + HpackLiteralSize(WellKnownHeaders.Scheme, request.RequestUri?.Scheme ?? "https")
                    + HpackLiteralSize(WellKnownHeaders.Authority, request.RequestUri?.Authority ?? "");
        foreach (var h in request.Headers)
        {
            hpack += HpackLiteralSize(h.Key, string.Join(WellKnownHeaders.CommaSpace, h.Value));
        }

        if (request.Content != null)
        {
            foreach (var h in request.Content.Headers)
            {
                hpack += HpackLiteralSize(h.Key, string.Join(WellKnownHeaders.CommaSpace, h.Value));
            }
        }

        var total = frameHeader + hpack;
        if (bodyLength > 0)
        {
            total += frameHeader + bodyLength;
        }

        return total;
    }

    // RFC 9113 §4.1: HEADERS frame (9-byte header + HPACK block) [+ DATA frame (9-byte header + body)]
    private static int Http2Response(HttpResponseMessage response, int bodyLength)
    {
        const int frameHeader = 9;
        var hpack = HpackLiteralSize(WellKnownHeaders.Status, ((int)response.StatusCode).ToString());
        foreach (var h in response.Headers)
        {
            hpack += HpackLiteralSize(h.Key, string.Join(WellKnownHeaders.CommaSpace, h.Value));
        }

        if (response.Content is not null)
        {
            foreach (var h in response.Content.Headers)
            {
                hpack += HpackLiteralSize(h.Key, string.Join(WellKnownHeaders.CommaSpace, h.Value));
            }
        }

        var total = frameHeader + hpack;
        if (bodyLength > 0)
        {
            total += frameHeader + bodyLength;
        }

        return total;
    }

    // RFC 9114 §7.1: HEADERS frame (Type(i)+Length(i)+QPACK block) [+ DATA frame (Type(i)+Length(i)+body)]
    private static int Http3Request(HttpRequestMessage request, int bodyLength)
    {
        // RFC 9204 §4.5.1: 2-byte QPACK header block prefix (required-insert-count=0, S=0, delta-base=0)
        const int qpackPrefix = 2;
        var payload = qpackPrefix
                      + QpackLiteralSize(WellKnownHeaders.Method, request.Method.Method)
                      + QpackLiteralSize(WellKnownHeaders.Path, request.ResolveTarget())
                      + QpackLiteralSize(WellKnownHeaders.Status, request.RequestUri?.Scheme ?? "https")
                      + QpackLiteralSize(WellKnownHeaders.Authority, request.RequestUri?.Authority ?? "");
        foreach (var h in request.Headers)
        {
            payload += QpackLiteralSize(h.Key, string.Join(WellKnownHeaders.CommaSpace, h.Value));
        }

        if (request.Content != null)
        {
            foreach (var h in request.Content.Headers)
            {
                payload += QpackLiteralSize(h.Key, string.Join(WellKnownHeaders.CommaSpace, h.Value));
            }
        }

        // frame type 0x01 (HEADERS) = 1 byte
        var total = 1 + QuicVarintSize(payload) + payload;
        if (bodyLength > 0)
        {
            // frame type 0x00 (DATA) = 1 byte
            total += 1 + QuicVarintSize(bodyLength) + bodyLength;
        }

        return total;
    }

    // RFC 9114 §7.1: HEADERS frame (Type(i)+Length(i)+QPACK block) [+ DATA frame (Type(i)+Length(i)+body)]
    private static int Http3Response(HttpResponseMessage response, int bodyLength)
    {
        const int qpackPrefix = 2;
        var payload = qpackPrefix + QpackLiteralSize(WellKnownHeaders.Status, ((int)response.StatusCode).ToString());
        foreach (var h in response.Headers)
        {
            payload += QpackLiteralSize(h.Key, string.Join(WellKnownHeaders.CommaSpace, h.Value));
        }

        if (response.Content != null)
        {
            foreach (var h in response.Content.Headers)
            {
                payload += QpackLiteralSize(h.Key, string.Join(WellKnownHeaders.CommaSpace, h.Value));
            }
        }

        var total = 1 + QuicVarintSize(payload) + payload;
        if (bodyLength > 0)
        {
            total += 1 + QuicVarintSize(bodyLength) + bodyLength;
        }

        return total;
    }

    // RFC 9112 §5: field-name ":" SP field-value CRLF per header + final CRLF
    private static int ResponseHeadersWireSize(HttpResponseMessage response)
    {
        var size = 0;
        foreach (var h in response.Headers)
        {
            size += h.Key.Length + 2 + string.Join(WellKnownHeaders.CommaSpace, h.Value).Length + 2;
        }

        if (response.Content is not null)
        {
            foreach (var h in response.Content.Headers)
            {
                size += h.Key.Length + 2 + string.Join(WellKnownHeaders.CommaSpace, h.Value).Length + 2;
            }
        }

        return size + 2; // final CRLF
    }

    // RFC 7541 §6.2.2: 0x00 prefix + name-string + value-string (literal no-indexing, no Huffman)
    // string = H(0) | length (7-bit prefix, 1 byte for length < 127) + octets
    private static int HpackLiteralSize(string name, string value)
        => 1 + (1 + name.Length) + (1 + value.Length);

    // RFC 9204 §4.5.5: literal field line without name reference (no Huffman, static-only)
    // Uses same string encoding as HPACK (H-bit + 7-bit length prefix)
    private static int QpackLiteralSize(string name, string value)
        => 1 + (1 + name.Length) + (1 + value.Length);

    // RFC 9000 §16: QUIC variable-length integer encoding
    private static int QuicVarintSize(int n)
    {
        return n switch
        {
            < 64 => 1,
            < 16 * 1024 => 2,
            < 1_073_741_824 => 4,
            _ => 8
        };
    }
}