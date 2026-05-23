using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Context.Features;

namespace TurboHTTP.Server;

internal static class ServerContextFactory
{
    public static TurboHttpContext Create(
        TurboHttpRequestFeature requestFeature,
        bool hasBody,
        IServiceProvider? services = null,
        TurboConnectionInfo? connectionInfo = null,
        TlsHandshakeFeature? tlsFeature = null)
    {
        var features = new FeatureCollection();
        features.Set<IHttpRequestFeature>(requestFeature);

        var bodyFeature = new TurboRequestBodyFeature { Body = requestFeature.Body };
        features.Set<ITurboRequestBodyFeature>(bodyFeature);

        features.Set<IHttpResponseFeature>(new TurboHttpResponseFeature());
        features.Set<IHttpRequestBodyDetectionFeature>(new TurboHttpRequestBodyDetectionFeature(hasBody));
        var responseBodyFeature = new TurboHttpResponseBodyFeature();
        features.Set<IHttpResponseBodyFeature>(responseBodyFeature);
        features.Set<ITurboResponseBodyFeature>(responseBodyFeature);

        if (tlsFeature is not null)
        {
            features.Set<ITlsHandshakeFeature>(tlsFeature);
        }

        TurboHttpContext ctx;
        if (connectionInfo is not null)
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

        features.Set<IHttpRequestLifetimeFeature>(new TurboHttpRequestLifetimeFeature(ctx));
        features.Set<IHttpRequestIdentifierFeature>(new TurboHttpRequestIdentifierFeature(ctx));

        return ctx;
    }
}
