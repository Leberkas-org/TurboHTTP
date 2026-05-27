using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Context.Features;

namespace TurboHTTP.Server;

internal static class FeatureCollectionFactory
{
    [ThreadStatic]
    private static Stack<TurboFeatureCollection>? t_pool;

    private const int MaxPoolSize = 32;

    public static IFeatureCollection Create(
        TurboHttpRequestFeature requestFeature,
        bool hasBody,
        IServiceProvider? services = null,
        IHttpConnectionFeature? connectionFeature = null,
        TlsHandshakeFeature? tlsFeature = null,
        long? maxRequestBodySize = null)
    {
        TurboFeatureCollection features;

        if ((t_pool?.Count ?? 0) > 0)
        {
            features = t_pool!.Pop();
        }
        else
        {
            features = new TurboFeatureCollection();
        }

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

        if (connectionFeature is not null)
        {
            features.Set<IHttpConnectionFeature>(connectionFeature);
        }

        if (tlsFeature is not null)
        {
            features.Set<ITlsHandshakeFeature>(tlsFeature);
        }

        var lifetimeFeature = new TurboHttpRequestLifetimeFeature();
        features.Set<IHttpRequestLifetimeFeature>(lifetimeFeature);

        var identifierFeature = new TurboHttpRequestIdentifierFeature();
        features.Set<IHttpRequestIdentifierFeature>(identifierFeature);

        var maxBodyFeature = new TurboHttpMaxRequestBodySizeFeature
        {
            MaxRequestBodySize = maxRequestBodySize
        };
        features.Set<IHttpMaxRequestBodySizeFeature>(maxBodyFeature);

        var bodyControlFeature = new TurboHttpBodyControlFeature();
        features.Set<IHttpBodyControlFeature>(bodyControlFeature);

        return features;
    }

    internal static void Return(IFeatureCollection features)
    {
        if (features is not TurboFeatureCollection turboFeatures)
        {
            return;
        }

        t_pool ??= new Stack<TurboFeatureCollection>(MaxPoolSize);

        if (t_pool.Count < MaxPoolSize)
        {
            t_pool.Push(turboFeatures);
        }
    }
}
