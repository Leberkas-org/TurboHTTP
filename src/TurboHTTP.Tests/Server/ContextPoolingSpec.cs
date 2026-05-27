using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Context;
using TurboHTTP.Context.Features;
using TurboHTTP.Server;

namespace TurboHTTP.Tests.Server;

public sealed class ContextPoolingSpec
{
    private static TurboHttpContext CreateContext(IFeatureCollection? features = null)
    {
        features ??= new FeatureCollection();
        features.Set<IHttpRequestFeature>(new TurboHttpRequestFeature());
        features.Set<IHttpResponseFeature>(new TurboHttpResponseFeature());
        features.Set<IHttpResponseBodyFeature>(new TurboHttpResponseBodyFeature());
        var connectionInfo = new TurboConnectionInfo(
            "test-id",
            null,
            0,
            null,
            0);

        var ctx = new TurboHttpContext(
            features,
            connectionInfo,
            services: null,
            requestAborted: CancellationToken.None,
            materializer: null!);

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
    public void TurboHttpContext_Reset_clears_user()
    {
        var ctx = CreateContext();
        ctx.User = new System.Security.Claims.ClaimsPrincipal();

        var newFeatures = new FeatureCollection();
        newFeatures.Set<IHttpRequestFeature>(new TurboHttpRequestFeature());
        newFeatures.Set<IHttpResponseFeature>(new TurboHttpResponseFeature());
        newFeatures.Set<IHttpResponseBodyFeature>(new TurboHttpResponseBodyFeature());

        var newConnectionInfo = new TurboConnectionInfo("new-id", null, 0, null, 0);
        ctx.Reset(newFeatures, newConnectionInfo, null, CancellationToken.None, null!);

        Assert.NotNull(ctx.User);
    }

    [Fact(Timeout = 5000)]
    public void TurboHttpContext_Reset_clears_items()
    {
        var ctx = CreateContext();
        ctx.Items["key"] = "value";

        var newFeatures = new FeatureCollection();
        newFeatures.Set<IHttpRequestFeature>(new TurboHttpRequestFeature());
        newFeatures.Set<IHttpResponseFeature>(new TurboHttpResponseFeature());
        newFeatures.Set<IHttpResponseBodyFeature>(new TurboHttpResponseBodyFeature());

        var newConnectionInfo = new TurboConnectionInfo("new-id", null, 0, null, 0);
        ctx.Reset(newFeatures, newConnectionInfo, null, CancellationToken.None, null!);

        Assert.Empty(ctx.Items);
    }

    [Fact(Timeout = 5000)]
    public void TurboHttpContext_Reset_clears_trace_identifier()
    {
        var ctx = CreateContext();
        _ = ctx.TraceIdentifier;

        var newFeatures = new FeatureCollection();
        newFeatures.Set<IHttpRequestFeature>(new TurboHttpRequestFeature());
        newFeatures.Set<IHttpResponseFeature>(new TurboHttpResponseFeature());
        newFeatures.Set<IHttpResponseBodyFeature>(new TurboHttpResponseBodyFeature());

        var newConnectionInfo = new TurboConnectionInfo("new-id", null, 0, null, 0);
        ctx.Reset(newFeatures, newConnectionInfo, null, CancellationToken.None, null!);

        Assert.NotEqual("", ctx.TraceIdentifier);
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