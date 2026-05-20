using Akka;
using Akka.Streams.Dsl;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Context.Adapters;

namespace TurboHTTP.Context.Features;

internal sealed class TurboHttpRequestFeature(
    HttpRequestMessage request,
    Source<ReadOnlyMemory<byte>, NotUsed> bodySource)
    : IHttpRequestFeature, ITurboRequestBodyFeature
{
    public string Protocol
    {
        get
        {
            return RequestMessage.Version switch
            {
                { Major: 1, Minor: 0 } => "HTTP/1.0",
                { Major: 1, Minor: 1 } => "HTTP/1.1",
                { Major: 2 } => "HTTP/2",
                { Major: 3 } => "HTTP/3",
                _ => "HTTP/1.1"
            };
        }
        set { }
    }

    public string Scheme
    {
        get => RequestMessage.RequestUri is { IsAbsoluteUri: true } uri ? uri.Scheme : "http";
        set { }
    }

    public string Method
    {
        get => RequestMessage.Method.Method;
        set => RequestMessage.Method = new HttpMethod(value);
    }

    public string PathBase
    {
        get => string.Empty;
        set { }
    }

    public string Path
    {
        get
        {
            if (RequestMessage.RequestUri == null)
            {
                return "/";
            }

            if (RequestMessage.RequestUri.IsAbsoluteUri)
            {
                var path = RequestMessage.RequestUri.AbsolutePath;
                return string.IsNullOrEmpty(path) ? "/" : path;
            }

            var original = RequestMessage.RequestUri.OriginalString;
            var queryIdx = original.IndexOf('?');
            var pathPart = queryIdx >= 0 ? original[..queryIdx] : original;
            return string.IsNullOrEmpty(pathPart) ? "/" : pathPart;
        }
        set { }
    }

    public string QueryString
    {
        get
        {
            if (RequestMessage.RequestUri == null)
            {
                return string.Empty;
            }

            if (RequestMessage.RequestUri.IsAbsoluteUri)
            {
                var query = RequestMessage.RequestUri.Query;
                return string.IsNullOrEmpty(query) ? string.Empty : query;
            }

            var original = RequestMessage.RequestUri.OriginalString;
            var queryIdx = original.IndexOf('?');
            return queryIdx >= 0 ? original[queryIdx..] : string.Empty;
        }
        set { }
    }

    public string RawTarget
    {
        get => RequestMessage.RequestUri?.OriginalString ?? "/";
        set { }
    }

    public IHeaderDictionary Headers
    {
        get
        {
            field ??= new TurboRequestHeaderDictionary(
                RequestMessage.Headers,
                RequestMessage.Content?.Headers);
            return field;
        }
        set { }
    }

    public Stream Body
    {
        get => RequestMessage.Content?.ReadAsStream() ?? Stream.Null;
        set { }
    }

    public Source<ReadOnlyMemory<byte>, NotUsed> BodySource { get; } =
        bodySource ?? throw new ArgumentNullException(nameof(bodySource));

    internal HttpRequestMessage RequestMessage { get; } = request ?? throw new ArgumentNullException(nameof(request));
}