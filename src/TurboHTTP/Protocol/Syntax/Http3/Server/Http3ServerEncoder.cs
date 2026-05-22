using TurboHTTP.Protocol.Syntax.Http3.Qpack;
using TurboHTTP.Server;

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
    public HeadersFrame EncodeHeaders(TurboHttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _reusableHeaders.Clear();
        BuildHeaderList(context, _reusableHeaders);

        var headerBlock = _tableSync.Encoder.Encode(_reusableHeaders);

        return new HeadersFrame(headerBlock);
    }

    private static void BuildHeaderList(TurboHttpContext context, List<(string Name, string Value)> headers)
    {
        // RFC 9114 §6.3: :status pseudo-header (required, must be first)
        headers.Add((WellKnownHeaders.Status, context.Response.StatusCode.ToString()));

        // Add regular headers (lowercase per RFC 9114)
        foreach (var h in context.Response.Headers)
        {
            if (!ContentHeaderClassifier.IsForbiddenConnectionHeader(h.Key))
            {
                var value = h.Value.Count == 1 ? h.Value[0]! : string.Join(", ", h.Value.ToArray());
                headers.Add((ContentHeaderClassifier.ToLowerAscii(h.Key), value));
            }
        }
    }

}