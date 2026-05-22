using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Context.Features;
using TurboHTTP.Server;

namespace TurboHTTP.Tests.Shared;

internal static class ServerTestContext
{
    internal static ServerTestContextBuilder Request() => new();

    internal static TurboHttpContext CreateResponse(int statusCode = 200)
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

    internal static TurboHttpContext CreateH2Response(int streamId, int statusCode = 200)
    {
        var ctx = CreateResponse(statusCode);
        ctx.Features.Set<IHttpStreamIdFeature>(new TurboStreamIdFeature(streamId));
        return ctx;
    }

    internal static TurboHttpContext CreateH3Response(long streamId, int statusCode = 200)
    {
        var ctx = CreateResponse(statusCode);
        ctx.Features.Set<IHttpStreamIdFeature>(new TurboStreamIdFeature(streamId));
        return ctx;
    }
}
