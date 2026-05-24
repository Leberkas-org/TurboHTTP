using System.Net;
using Akka.Actor;
using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Context.Features;
using TurboHTTP.Protocol.LineBased;
using TurboHTTP.Protocol.LineBased.Body;
using TurboHTTP.Protocol.Semantics;
using TurboHTTP.Protocol.Syntax.Http10.Options;
using TurboHTTP.Server;

namespace TurboHTTP.Protocol.Syntax.Http10.Server;

internal sealed class Http10ServerEncoder
{
    private readonly Http10ServerEncoderOptions _options;
    private readonly HeaderCollection _reusableHeaders = new();

    public Http10ServerEncoder(Http10ServerEncoderOptions options)
    {
        options.Validate();
        _options = options;
    }

    public int Encode(Span<byte> _, TurboHttpContext context, IActorRef stageActor)
    {
        // HTTP/1.0 always defers — body sink will be handled by caller
        return 0;
    }

    public int EncodeDeferred(Span<byte> destination, TurboHttpContext context, ReadOnlySpan<byte> body)
    {
        var writer = SpanWriter.Create(destination);
        StatusLineWriter.Write(ref writer, HttpVersion.Version10, context.Response.StatusCode);

        _reusableHeaders.Clear();
        var headers = _reusableHeaders;
        foreach (var h in context.Response.Headers)
        {
            if (ConnectionSemantics.IsHopByHop(h.Key))
            {
                continue;
            }

            foreach (var v in h.Value)
            {
                if (v is not null)
                {
                    headers.Add(h.Key, v);
                }
            }
        }

        headers.Add(WellKnownHeaders.ContentLength, ContentLengthCache.GetValue(body.Length));

        if (_options.WriteDateHeader && !headers.Contains(WellKnownHeaders.Date))
        {
            headers.Add(WellKnownHeaders.Date, DateHeaderCache.GetValue());
        }

        HeaderBlockWriter.Write(ref writer, headers);

        if (body.Length > 0)
        {
            writer.WriteBytes(body);
        }

        return writer.BytesWritten;
    }
}