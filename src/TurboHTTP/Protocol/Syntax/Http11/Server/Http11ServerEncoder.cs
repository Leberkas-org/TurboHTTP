using System.Net;
using Akka.Actor;
using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Context;
using TurboHTTP.Protocol.LineBased;
using TurboHTTP.Protocol.LineBased.Body;
using TurboHTTP.Protocol.Semantics;
using TurboHTTP.Protocol.Syntax.Http11.Options;

namespace TurboHTTP.Protocol.Syntax.Http11.Server;

internal sealed class Http11ServerEncoder
{
    private readonly Http11ServerEncoderOptions _options;
    private readonly HeaderCollection _reusableHeaders = new();
    private IBodyEncoder? _activeBodyEncoder;

    public Http11ServerEncoder(Http11ServerEncoderOptions options)
    {
        options.Validate();
        _options = options;
    }

    public void SetActiveBodyEncoder(IBodyEncoder encoder)
    {
        _activeBodyEncoder?.Dispose();
        _activeBodyEncoder = encoder;
    }

    public void CancelActiveBody()
    {
        _activeBodyEncoder?.Dispose();
        _activeBodyEncoder = null;
    }

    public int Encode(Span<byte> destination, IFeatureCollection features, bool isChunked = false, bool connectionClose = false)
    {
        var writer = SpanWriter.Create(destination);

        var responseFeature = features.Get<IHttpResponseFeature>();
        var statusCode = responseFeature?.StatusCode ?? 500;
        StatusLineWriter.Write(ref writer, HttpVersion.Version11, statusCode);

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

        if (isChunked)
        {
            headers.Add(WellKnownHeaders.TransferEncoding, WellKnownHeaders.ChunkedValue);
        }
        else
        {
            var contentLengthFeature = features.Get<IHttpResponseBodyFeature>();
            var contentLength = 0L;
            headers.Add(WellKnownHeaders.ContentLength, ContentLengthCache.GetValue(contentLength));
        }

        if (_options.WriteDateHeader && !headers.Contains(WellKnownHeaders.Date))
        {
            headers.Add(WellKnownHeaders.Date, DateHeaderCache.GetValue());
        }

        if (connectionClose)
        {
            headers.Add(WellKnownHeaders.Connection, WellKnownHeaders.CloseValue);
        }

        HeaderBlockWriter.Write(ref writer, headers);

        // Body encoding is handled separately via the BodySink
        return writer.BytesWritten;
    }
}