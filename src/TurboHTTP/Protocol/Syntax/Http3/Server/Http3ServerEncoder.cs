using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Protocol.Syntax.Http3.Qpack;
using TurboHTTP.Server;
using TurboHTTP.Streams.Stages.Server;

namespace TurboHTTP.Protocol.Syntax.Http3.Server;

/// <summary>
/// Encodes HTTP/3 response messages into HEADERS and DATA frame sequences.
/// Mirrors the client's Http3ClientEncoder but produces responses instead of requests.
/// Stateful: maintains QPACK encoder for connection lifetime.
/// </summary>
internal sealed class Http3ServerEncoder
{
    private readonly QpackTableSync _tableSync;
    private readonly List<(string Name, string Value)> _reusableHeaders = new(16);

    public Http3ServerEncoder(QpackTableSync tableSync)
    {
        ArgumentNullException.ThrowIfNull(tableSync);
        _tableSync = tableSync;
    }

    /// <summary>
    /// Encoder instructions generated during the most recent response encode.
    /// These must be sent on the encoder instruction stream before the response is transmitted.
    /// </summary>
    public ReadOnlyMemory<byte> EncoderInstructions =>
        _tableSync.Encoder.EncoderInstructions;

    /// <summary>
    /// Encodes a response to HTTP/3 HEADERS frame only.
    /// Body is handled asynchronously via IBodyEncoder and StreamState outbound buffer.
    /// </summary>
    public HeadersFrame EncodeHeaders(RequestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _reusableHeaders.Clear();
        BuildHeaderList(context, _reusableHeaders);

        var headerBlock = _tableSync.Encoder.Encode(_reusableHeaders);

        return new HeadersFrame(headerBlock);
    }

    private static void BuildHeaderList(RequestContext context, List<(string Name, string Value)> headers)
    {
        // RFC 9114 §6.3: :status pseudo-header (required, must be first)
        var responseFeature = context.Features.Get<IHttpResponseFeature>();
        var statusCode = responseFeature?.StatusCode ?? 500;
        headers.Add((WellKnownHeaders.Status, WellKnownHeaders.GetStatusCodeString(statusCode)));

        // Add regular headers (lowercase per RFC 9114)
        var responseHeaders = responseFeature?.Headers;
        if (responseHeaders is not null)
        {
            foreach (var h in responseHeaders)
            {
                if (!ContentHeaderClassifier.IsForbiddenConnectionHeader(h.Key))
                {
                    var value = h.Value.Count == 1 ? h.Value[0]! : string.Join(", ", h.Value);
                    headers.Add((ContentHeaderClassifier.ToLowerAscii(h.Key), value));
                }
            }
        }
    }

}