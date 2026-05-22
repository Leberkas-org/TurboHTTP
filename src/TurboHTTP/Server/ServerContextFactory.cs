using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Context.Features;

namespace TurboHTTP.Server;

internal static class ServerContextFactory
{
    public static TurboHttpContext Create(TurboHttpRequestFeature requestFeature, bool hasBody)
    {
        var features = new FeatureCollection();
        features.Set<IHttpRequestFeature>(requestFeature);
        features.Set<ITurboRequestBodyFeature>(requestFeature);
        features.Set<IHttpResponseFeature>(new TurboHttpResponseFeature());
        features.Set<IHttpRequestBodyDetectionFeature>(new TurboHttpRequestBodyDetectionFeature(hasBody));
        var bodyFeature = new TurboHttpResponseBodyFeature();
        features.Set<IHttpResponseBodyFeature>(bodyFeature);
        features.Set<ITurboResponseBodyFeature>(bodyFeature);
        return new TurboHttpContext(features);
    }
}
