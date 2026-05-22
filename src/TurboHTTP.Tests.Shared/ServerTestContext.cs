using Akka.Streams.Dsl;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core.Features;
using TurboHTTP.Context.Features;
using TurboHTTP.Server;

namespace TurboHTTP.Tests.Shared;

/// <summary>
/// Test helper for building TurboHttpContext instances and request features in unit tests.
/// </summary>
internal static class ServerTestContext
{
    /// <summary>
    /// Creates a TurboHttpRequestFeature from an HttpRequestMessage.
    /// </summary>
    /// <param name="request">The HttpRequestMessage to convert.</param>
    /// <param name="bodySource">The body stream source. Defaults to empty.</param>
    /// <returns>A TurboHttpRequestFeature initialized from the request message.</returns>
    public static TurboHttpRequestFeature CreateRequestFeature(
        HttpRequestMessage request,
        Source<ReadOnlyMemory<byte>, global::Akka.NotUsed>? bodySource = null)
    {
        bodySource ??= Source.Empty<ReadOnlyMemory<byte>>();

        var uri = request.RequestUri ?? throw new InvalidOperationException("RequestUri cannot be null");
        var scheme = uri.Scheme;
        var path = uri.AbsolutePath;
        var query = uri.Query;
        var host = uri.Host;
        if (uri.IsDefaultPort == false)
        {
            host = string.Concat(host, ":", uri.Port);
        }

        var feature = new TurboHttpRequestFeature
        {
            Method = request.Method.Method,
            Scheme = scheme,
            Path = path,
            QueryString = query,
            RawTarget = string.IsNullOrEmpty(query) ? path : string.Concat(path, query),
            Protocol = $"HTTP/{request.Version.Major}.{request.Version.Minor}",
            Headers = new Microsoft.Extensions.Primitives.HeaderDictionary(),
            Body = Stream.Null,
            BodySource = bodySource,
            ExtractedHost = host
        };

        foreach (var (name, values) in request.Headers)
        {
            feature.Headers[name] = new Microsoft.Extensions.Primitives.StringValues(values.ToArray());
        }

        if (request.Content?.Headers != null)
        {
            foreach (var (name, values) in request.Content.Headers)
            {
                feature.Headers[name] = new Microsoft.Extensions.Primitives.StringValues(values.ToArray());
            }
        }

        return feature;
    }

    /// <summary>
    /// Creates a basic TurboHttpContext with a response feature.
    /// </summary>
    /// <param name="statusCode">The HTTP status code. Default is 200.</param>
    /// <returns>A configured TurboHttpContext.</returns>
    public static TurboHttpContext CreateResponse(int statusCode = 200)
    {
        var features = new FeatureCollection();
        features.Set<IHttpRequestFeature>(new TurboHttpRequestFeature());
        var responseFeature = new TurboHttpResponseFeature { StatusCode = statusCode };
        features.Set<IHttpResponseFeature>(responseFeature);
        var bodyFeature = new TurboHttpResponseBodyFeature();
        features.Set<IHttpResponseBodyFeature>(bodyFeature);
        features.Set<ITurboResponseBodyFeature>(bodyFeature);
        return new TurboHttpContext(features);
    }

    /// <summary>
    /// Creates a TurboHttpContext with H2 stream ID feature.
    /// </summary>
    /// <param name="streamId">The HTTP/2 stream ID.</param>
    /// <param name="statusCode">The HTTP status code. Default is 200.</param>
    /// <returns>A configured TurboHttpContext with H2 stream ID.</returns>
    public static TurboHttpContext CreateH2Response(int streamId, int statusCode = 200)
    {
        var ctx = CreateResponse(statusCode);
        ctx.Features.Set<IHttp2StreamIdFeature>(new TurboHttp2StreamIdFeature(streamId));
        return ctx;
    }

    /// <summary>
    /// Creates a TurboHttpContext with H3 stream ID feature.
    /// </summary>
    /// <param name="streamId">The HTTP/3 stream ID.</param>
    /// <param name="statusCode">The HTTP status code. Default is 200.</param>
    /// <returns>A configured TurboHttpContext with H3 stream ID.</returns>
    public static TurboHttpContext CreateH3Response(long streamId, int statusCode = 200)
    {
        var ctx = CreateResponse(statusCode);
        ctx.Features.Set<ITurboHttp3StreamIdFeature>(new TurboHttp3StreamIdFeature(streamId));
        return ctx;
    }
}
