using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Context.Features;

namespace TurboHTTP.Tests.Shared;

internal static class ServerTestContext
{
    internal static ServerTestContextBuilder Request() => new();

    internal static IFeatureCollection CreateResponse(int statusCode = 200)
    {
        var features = new TurboFeatureCollection();
        features.Set<IHttpRequestFeature>(new TurboHttpRequestFeature());
        var responseFeature = new TurboHttpResponseFeature { StatusCode = statusCode };
        features.Set<IHttpResponseFeature>(responseFeature);
        var bodyFeature = new TurboHttpResponseBodyFeature();
        features.Set<IHttpResponseBodyFeature>(bodyFeature);
        return features;
    }

    internal static IFeatureCollection CreateH2Response(int streamId, int statusCode = 200)
    {
        var features = CreateResponse(statusCode);
        features.Set<IHttpStreamIdFeature>(new TurboStreamIdFeature(streamId));
        return features;
    }

    internal static IFeatureCollection CreateH3Response(long streamId, int statusCode = 200)
    {
        var features = CreateResponse(statusCode);
        features.Set<IHttpStreamIdFeature>(new TurboStreamIdFeature(streamId));
        return features;
    }
}
