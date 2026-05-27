using System.Net;
using Akka.Actor;
using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Context;
using TurboHTTP.Context.Features;
using TurboHTTP.Protocol.LineBased;
using TurboHTTP.Protocol.LineBased.Body;
using TurboHTTP.Protocol.Semantics;
using TurboHTTP.Protocol.Syntax.Http10.Options;
using TurboHTTP.Server;
using TurboHTTP.Streams.Stages.Server;

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

    public int Encode(Span<byte> _, RequestContext context, IActorRef stageActor)
    {
        // HTTP/1.0 always defers — body sink will be handled by caller
        return 0;
    }

    public int EncodeDeferred(Span<byte> destination, RequestContext context, ReadOnlySpan<byte> body)
    {
        var writer = SpanWriter.Create(destination);
        var responseFeature = context.Features.Get<IHttpResponseFeature>();
        var statusCode = responseFeature?.StatusCode ?? 500;
        StatusLineWriter.Write(ref writer, HttpVersion.Version10, statusCode);

        _reusableHeaders.Clear();
        var headers = _reusableHeaders;
        var responseHeaders = responseFeature?.Headers;
        if (responseHeaders is not null)
        {
            foreach (var h in responseHeaders)
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