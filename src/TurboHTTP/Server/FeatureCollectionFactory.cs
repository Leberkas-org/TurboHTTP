using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Server.Context.Features;

namespace TurboHTTP.Server;

internal static class FeatureCollectionFactory
{
    [ThreadStatic] private static Stack<TurboFeatureCollection>? _tPool;

    private const int MaxPoolSize = 32;

    public static IFeatureCollection Create(
        TurboHttpRequestFeature requestFeature,
        bool hasBody,
        IServiceProvider? services = null,
        IHttpConnectionFeature? connectionFeature = null,
        TlsHandshakeFeature? tlsFeature = null,
        long? maxRequestBodySize = null)
    {
        var features = (_tPool?.Count ?? 0) > 0 ? _tPool!.Pop() : new TurboFeatureCollection();

        features.Set<IHttpRequestFeature>(requestFeature);

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
            features.Set(connectionFeature);
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

        turboFeatures.RequestTimestamp = 0;
        turboFeatures.RequestActivity = null;

        _tPool ??= new Stack<TurboFeatureCollection>(MaxPoolSize);

        if (_tPool.Count < MaxPoolSize)
        {
            _tPool.Push(turboFeatures);
        }
    }
}