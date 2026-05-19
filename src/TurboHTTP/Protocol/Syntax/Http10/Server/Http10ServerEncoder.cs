using System.Globalization;
using System.Net;
using Akka.Actor;
using TurboHTTP.Protocol.LineBased;
using TurboHTTP.Protocol.LineBased.Body;
using TurboHTTP.Protocol.Semantics;
using TurboHTTP.Protocol.Syntax.Http10.Options;

namespace TurboHTTP.Protocol.Syntax.Http10.Server;

internal sealed class Http10ServerEncoder
{
    private readonly Http10ServerEncoderOptions _options;

    public Http10ServerEncoder(Http10ServerEncoderOptions options)
    {
        options.Validate();
        _options = options;
    }

    public int Encode(Span<byte> _, HttpResponseMessage response, IActorRef stageActor)
    {
        // HTTP/1.0 always defers — buffer body to learn Content-Length
        var bodyEncoder = new ContentLengthBufferedBodyEncoder();
        bodyEncoder.Start(response.Content, stageActor);
        return 0;
    }

    public int EncodeDeferred(Span<byte> destination, HttpResponseMessage response, ReadOnlySpan<byte> body)
    {
        var writer = SpanWriter.Create(destination);
        StatusLineWriter.Write(ref writer, HttpVersion.Version10, (int)response.StatusCode);

        var headers = new HeaderCollection();
        foreach (var h in response.Headers)
        {
            if (ConnectionSemantics.IsHopByHop(h.Key))
            {
                continue;
            }

            foreach (var v in h.Value)
            {
                headers.Add(h.Key, v);
            }
        }

        foreach (var h in response.Content.Headers)
        {
            if (string.Equals(h.Key, WellKnownHeaders.ContentLength, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (ConnectionSemantics.IsHopByHop(h.Key))
            {
                continue;
            }

            foreach (var v in h.Value)
            {
                headers.Add(h.Key, v);
            }
        }

        headers.Add(WellKnownHeaders.ContentLength, body.Length.ToString(CultureInfo.InvariantCulture));

        if (_options.WriteDateHeader && !headers.Contains(WellKnownHeaders.Date))
        {
            headers.Add(WellKnownHeaders.Date, DateTime.UtcNow.ToString("r", CultureInfo.InvariantCulture));
        }

        HeaderBlockWriter.Write(ref writer, headers);

        if (body.Length > 0)
        {
            writer.WriteBytes(body);
        }

        return writer.BytesWritten;
    }
}