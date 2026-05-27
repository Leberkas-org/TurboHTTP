using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Context.Features;
using TurboHTTP.Streams.Stages.Server;

namespace TurboHTTP.Server;

internal static class ServerContextFactory
{
    [ThreadStatic]
    private static Stack<RequestContext>? t_pool;

    private const int MaxPoolSize = 32;

    public static RequestContext Create(
        TurboHttpRequestFeature requestFeature,
        bool hasBody,
        IServiceProvider? services = null,
        TurboConnectionInfo? connectionInfo = null,
        TlsHandshakeFeature? tlsFeature = null)
    {
        var features = new TurboFeatureCollection();

        features.Set<IHttpRequestFeature>(requestFeature);

        var bodyFeature = new TurboRequestBodyFeature { Body = requestFeature.Body };
        features.Set<TurboRequestBodyFeature>(bodyFeature);

        var responseFeature = new TurboHttpResponseFeature();
        features.Set<IHttpResponseFeature>(responseFeature);

        var detectionFeature = new TurboHttpRequestBodyDetectionFeature(hasBody);
        features.Set<IHttpRequestBodyDetectionFeature>(detectionFeature);

        var responseBodyFeature = new TurboHttpResponseBodyFeature();
        features.Set<IHttpResponseBodyFeature>(responseBodyFeature);

        var trailersFeature = new TurboHttpResponseTrailersFeature();
        features.Set<IHttpResponseTrailersFeature>(trailersFeature);

        if (tlsFeature is not null)
        {
            features.Set<ITlsHandshakeFeature>(tlsFeature);
        }

        RequestContext context;

        if ((t_pool?.Count ?? 0) > 0)
        {
            context = t_pool!.Pop();
            context.Features = features;
            context.Lifetime = null;
        }
        else
        {
            context = new RequestContext { Features = features };
        }

        var lifetimeFeature = new TurboHttpRequestLifetimeFeature(context);
        features.Set<IHttpRequestLifetimeFeature>(lifetimeFeature);

        var identifierFeature = new TurboHttpRequestIdentifierFeature(context);
        features.Set<IHttpRequestIdentifierFeature>(identifierFeature);

        return context;
    }

    internal static void Return(RequestContext context)
    {
        t_pool ??= new Stack<RequestContext>(MaxPoolSize);

        if (t_pool.Count < MaxPoolSize)
        {
            t_pool.Push(context);
        }
    }
}
