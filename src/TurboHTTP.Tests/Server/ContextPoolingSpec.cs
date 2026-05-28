using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Server;
using TurboHTTP.Server.Context.Features;

namespace TurboHTTP.Tests.Server;

public sealed class ContextPoolingSpec
{
    private static IFeatureCollection CreateContext(IFeatureCollection? features = null)
    {
        features ??= new FeatureCollection();
        features.Set<IHttpRequestFeature>(new TurboHttpRequestFeature());
        features.Set<IHttpResponseFeature>(new TurboHttpResponseFeature());
        features.Set<IHttpResponseBodyFeature>(new TurboHttpResponseBodyFeature());

        return features;
    }

    [Fact(Timeout = 5000)]
    public void TurboHttpResponseFeature_Reset_clears_status_code()
    {
        var feature = new TurboHttpResponseFeature
        {
            StatusCode = 404
        };

        feature.Reset();

        Assert.Equal(200, feature.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public void TurboHttpResponseFeature_Reset_clears_reason_phrase()
    {
        var feature = new TurboHttpResponseFeature
        {
            ReasonPhrase = "Not Found"
        };

        feature.Reset();

        Assert.Null(feature.ReasonPhrase);
    }

    [Fact(Timeout = 5000)]
    public void TurboHttpResponseFeature_Reset_clears_headers()
    {
        var feature = new TurboHttpResponseFeature
        {
            Headers =
            {
                ["Content-Type"] = "application/json"
            }
        };

        feature.Reset();

        Assert.Empty(feature.Headers);
    }

    [Fact(Timeout = 5000)]
    public void TurboHttpResponseFeature_Reset_clears_callbacks()
    {
        var feature = new TurboHttpResponseFeature();
        var callbackCalled = false;

        feature.OnStarting((_) =>
        {
            callbackCalled = true;
            return Task.CompletedTask;
        }, null!);

        feature.Reset();

        Assert.False(callbackCalled);
    }

    [Fact(Timeout = 5000)]
    public void TurboHttpResponseFeature_Reset_clears_has_started()
    {
        var feature = new TurboHttpResponseFeature();
        _ = feature.HasStarted;

        feature.Reset();

        Assert.False(feature.HasStarted);
    }

    [Fact(Timeout = 5000)]
    public void FeatureCollectionFactory_Return_stores_context_in_pool()
    {
        var ctx = CreateContext();

        FeatureCollectionFactory.Return(ctx);

        var ctx2 = FeatureCollectionFactory.Create(
            new TurboHttpRequestFeature(),
            hasBody: false,
            services: null,
            connectionFeature: null);

        Assert.NotNull(ctx2);
    }
}