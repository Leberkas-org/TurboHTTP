using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Context;
using TurboHTTP.Context.Features;
using TurboHTTP.Server;
using TurboHTTP.Streams.Stages.Server;

namespace TurboHTTP.Tests.Server;

public sealed class ContextPoolingSpec
{
    private static RequestContext CreateContext(IFeatureCollection? features = null)
    {
        features ??= new FeatureCollection();
        features.Set<IHttpRequestFeature>(new TurboHttpRequestFeature());
        features.Set<IHttpResponseFeature>(new TurboHttpResponseFeature());
        features.Set<IHttpResponseBodyFeature>(new TurboHttpResponseBodyFeature());
        var ctx = new RequestContext
        {
            Features = features,
            RequestAborted = CancellationToken.None
        };

        return ctx;
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
    public void TurboHttpRequest_Reset_clears_cached_uri()
    {
        var features = new TurboFeatureCollection();
        var headers = new HeaderDictionary { { "Host", "example.com" } };
        var requestFeature = new TurboHttpRequestFeature { Scheme = "https", Path = "/api", Headers = headers };
        features.Set<IHttpRequestFeature>(requestFeature);
        features.Set<IHttpResponseFeature>(new TurboHttpResponseFeature());
        features.Set<IHttpResponseBodyFeature>(new TurboHttpResponseBodyFeature());

        var request = new TurboHttpRequest(features);
        var originalUri = request.RequestUri;
        Assert.NotNull(originalUri);
        Assert.Equal("https://example.com/api", originalUri.ToString());

        var newHeaders = new HeaderDictionary { { "Host", "different.com" } };
        var newFeatures = new TurboFeatureCollection();
        newFeatures.Set<IHttpRequestFeature>(new TurboHttpRequestFeature
            { Scheme = "http", Path = "/test", Headers = newHeaders });
        newFeatures.Set<IHttpResponseFeature>(new TurboHttpResponseFeature());
        newFeatures.Set<IHttpResponseBodyFeature>(new TurboHttpResponseBodyFeature());

        request.Reset(newFeatures);

        var newUri = request.RequestUri;
        Assert.NotNull(newUri);
        Assert.Equal("http://different.com/test", newUri.ToString());
    }

    [Fact(Timeout = 5000)]
    public void ServerContextFactory_Return_stores_context_in_pool()
    {
        var ctx = CreateContext();

        ServerContextFactory.Return(ctx);

        var ctx2 = ServerContextFactory.Create(
            new TurboHttpRequestFeature(),
            hasBody: false,
            services: null,
            connectionInfo: new TurboConnectionInfo("id", null, 0, null, 0));

        Assert.NotNull(ctx2);
    }
}