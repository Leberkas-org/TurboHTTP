using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Context.Features;

namespace TurboHTTP.Server;

internal static class ServerContextFactory
{
    public static TurboHttpContext Create(
        TurboHttpRequestFeature requestFeature,
        bool hasBody,
        IServiceProvider? services = null,
        TurboConnectionInfo? connectionInfo = null)
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

        if (connectionInfo is not null)
        {
            if (connectionInfo.SecurityInfo is { } security)
            {
                features.Set<ITlsHandshakeFeature>(new TlsHandshakeFeature
                {
                    Protocol = security.Protocol,
                    NegotiatedCipherSuite = security.NegotiatedCipherSuite,
                    HostName = security.HostName,
                    NegotiatedApplicationProtocol = security.ApplicationProtocol,
                });
            }

            return new TurboHttpContext(features, connectionInfo, services, CancellationToken.None, null!);
        }

        var ctx = new TurboHttpContext(features);
        if (services is not null)
        {
            ctx.RequestServices = services;
        }

        return ctx;
    }
}
