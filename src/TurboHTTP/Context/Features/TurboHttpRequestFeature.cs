using Akka;
using Akka.Streams.Dsl;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Context.Adapters;

namespace TurboHTTP.Context.Features;

internal sealed class TurboHttpRequestFeature : IHttpRequestFeature, ITurboRequestBodyFeature
{
    public string Protocol { get; set; } = "HTTP/1.1";

    public string Scheme { get; set; } = "http";

    public string Method { get; set; } = "GET";

    public string PathBase { get; set; } = string.Empty;

    public string Path { get; set; } = "/";

    public string QueryString { get; set; } = string.Empty;

    public string RawTarget { get; set; } = "/";

    public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();

    public Stream Body { get; set; } = Stream.Null;

    public Source<ReadOnlyMemory<byte>, NotUsed> BodySource { get; init; } =
        Source.Empty<ReadOnlyMemory<byte>>();

    /// <summary>
    /// Stores the Host header value extracted from RequestUri.
    /// This is used by TurboHttpRequest.RequestUri to reconstruct the full URI.
    /// </summary>
    internal string? ExtractedHost { get; set; }

    internal static TurboHttpRequestFeature FromHttpRequestMessage(
        HttpRequestMessage request,
        Source<ReadOnlyMemory<byte>, NotUsed> bodySource)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(bodySource);

        var protocol = request.Version switch
        {
            { Major: 1, Minor: 0 } => "HTTP/1.0",
            { Major: 1, Minor: 1 } => "HTTP/1.1",
            { Major: 2 } => "HTTP/2",
            { Major: 3 } => "HTTP/3",
            _ => "HTTP/1.1"
        };

        var scheme = request.RequestUri is { IsAbsoluteUri: true } uri ? uri.Scheme : "http";

        var path = request.RequestUri == null
            ? "/"
            : request.RequestUri.IsAbsoluteUri
                ? string.IsNullOrEmpty(request.RequestUri.AbsolutePath)
                    ? "/"
                    : request.RequestUri.AbsolutePath
                : ExtractPathFromRelativeUri(request.RequestUri.OriginalString);

        var queryString = request.RequestUri == null
            ? string.Empty
            : request.RequestUri.IsAbsoluteUri
                ? string.IsNullOrEmpty(request.RequestUri.Query)
                    ? string.Empty
                    : request.RequestUri.Query
                : ExtractQueryStringFromRelativeUri(request.RequestUri.OriginalString);

        var rawTarget = request.RequestUri?.OriginalString ?? "/";

        var headers = new TurboRequestHeaderDictionary(
            request.Headers,
            request.Content?.Headers);

        // Extract host from RequestUri for later use in reconstructing full URIs
        string? extractedHost = null;
        if (request.RequestUri is { IsAbsoluteUri: true })
        {
            var host = request.RequestUri.Host;
            var port = request.RequestUri.Port;
            extractedHost = port == 80 || port == 443 ? host : string.Concat(host, ":", port);
        }

        var body = request.Content is not null ? request.Content.ReadAsStream() : Stream.Null;

        return new TurboHttpRequestFeature
        {
            Protocol = protocol,
            Scheme = scheme,
            Method = request.Method.Method,
            Path = path,
            QueryString = queryString,
            RawTarget = rawTarget,
            Headers = headers,
            Body = body,
            BodySource = bodySource,
            ExtractedHost = extractedHost
        };
    }

    private static string ExtractPathFromRelativeUri(string original)
    {
        var queryIdx = original.IndexOf('?');
        var pathPart = queryIdx >= 0 ? original[..queryIdx] : original;
        return string.IsNullOrEmpty(pathPart) ? "/" : pathPart;
    }

    private static string ExtractQueryStringFromRelativeUri(string original)
    {
        var queryIdx = original.IndexOf('?');
        return queryIdx >= 0 ? original[queryIdx..] : string.Empty;
    }
}