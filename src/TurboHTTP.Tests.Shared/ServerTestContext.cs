using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Context.Features;
using TurboHTTP.Server;
using TurboHTTP.Streams.Stages.Server;

namespace TurboHTTP.Tests.Shared;

internal static class ServerTestContext
{
    internal static ServerTestContextBuilder Request() => new();

    internal static RequestContext CreateResponse(int statusCode = 200)
    {
        var features = new TurboFeatureCollection();
        features.Set<IHttpRequestFeature>(new TurboHttpRequestFeature());
        var responseFeature = new TurboHttpResponseFeature { StatusCode = statusCode };
        features.Set<IHttpResponseFeature>(responseFeature);
        var bodyFeature = new TurboHttpResponseBodyFeature();
        features.Set<IHttpResponseBodyFeature>(bodyFeature);
        return new RequestContext { Features = features };
    }

    internal static RequestContext CreateH2Response(int streamId, int statusCode = 200)
    {
        var ctx = CreateResponse(statusCode);
        ctx.Features.Set<IHttpStreamIdFeature>(new TurboStreamIdFeature(streamId));
        return ctx;
    }

    internal static RequestContext CreateH3Response(long streamId, int statusCode = 200)
    {
        var ctx = CreateResponse(statusCode);
        ctx.Features.Set<IHttpStreamIdFeature>(new TurboStreamIdFeature(streamId));
        return ctx;
    }
}
