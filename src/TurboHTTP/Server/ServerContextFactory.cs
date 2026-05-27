using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Context.Features;

namespace TurboHTTP.Server;

internal static class ServerContextFactory
{
    [ThreadStatic]
    private static Stack<TurboHttpContext>? t_pool;

    private const int MaxPoolSize = 32;

    public static TurboHttpContext Create(
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

        TurboHttpContext ctx;
        var pooledConnection = connectionInfo is not null;
        var pooledServices = services is not null;

        if ((t_pool?.Count ?? 0) > 0 && pooledConnection && pooledServices)
        {
            ctx = t_pool!.Pop();
            ctx.Reset(features, connectionInfo!, services, CancellationToken.None, null!);
        }
        else if (pooledConnection)
        {
            ctx = new TurboHttpContext(features, connectionInfo, services, CancellationToken.None, null!);
        }
        else
        {
            ctx = new TurboHttpContext(features);
            if (services is not null)
            {
                ctx.RequestServices = services;
            }
        }

        var lifetimeFeature = new TurboHttpRequestLifetimeFeature(ctx);
        features.Set<IHttpRequestLifetimeFeature>(lifetimeFeature);

        var identifierFeature = new TurboHttpRequestIdentifierFeature(ctx);
        features.Set<IHttpRequestIdentifierFeature>(identifierFeature);

        return ctx;
    }

    internal static void Return(TurboHttpContext context)
    {
        t_pool ??= new Stack<TurboHttpContext>(MaxPoolSize);

        if (t_pool.Count < MaxPoolSize)
        {
            t_pool.Push(context);
        }
    }
}
