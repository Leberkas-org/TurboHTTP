using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Server;
using TurboHTTP.Server.Context.Features;

namespace TurboHTTP.Tests.Server;

public sealed class FeatureCollectionFactorySpec
{
    [Fact(Timeout = 5000)]
    public void Create_should_set_request_feature()
    {
        var requestFeature = new TurboHttpRequestFeature { Method = "POST", Path = "/api" };
        var ctx = FeatureCollectionFactory.Create(requestFeature, hasBody: false);

        var reqFeature = ctx.Get<IHttpRequestFeature>()!;
        Assert.Equal("POST", reqFeature.Method);
        Assert.Equal("/api", reqFeature.Path);
    }

    [Fact(Timeout = 5000)]
    public void Create_should_set_response_feature()
    {
        var requestFeature = new TurboHttpRequestFeature();
        var ctx = FeatureCollectionFactory.Create(requestFeature, hasBody: false);

        var responseFeature = ctx.Get<IHttpResponseFeature>();
        Assert.NotNull(responseFeature);
    }

    [Fact(Timeout = 5000)]
    public void Create_should_set_body_detection_true_when_has_body()
    {
        var requestFeature = new TurboHttpRequestFeature();
        var ctx = FeatureCollectionFactory.Create(requestFeature, hasBody: true);

        var detection = ctx.Get<IHttpRequestBodyDetectionFeature>();
        Assert.NotNull(detection);
        Assert.True(detection.CanHaveBody);
    }

    [Fact(Timeout = 5000)]
    public void Create_should_set_body_detection_false_when_no_body()
    {
        var requestFeature = new TurboHttpRequestFeature();
        var ctx = FeatureCollectionFactory.Create(requestFeature, hasBody: false);

        var detection = ctx.Get<IHttpRequestBodyDetectionFeature>();
        Assert.NotNull(detection);
        Assert.False(detection.CanHaveBody);
    }

    [Fact(Timeout = 5000)]
    public void Create_should_set_response_body_feature()
    {
        var requestFeature = new TurboHttpRequestFeature();
        var ctx = FeatureCollectionFactory.Create(requestFeature, hasBody: false);

        var responseBodyFeature = ctx.Get<IHttpResponseBodyFeature>();
        Assert.NotNull(responseBodyFeature);

        var turboResponseBody = ctx.Get<IHttpResponseBodyFeature>();
        Assert.NotNull(turboResponseBody);
    }

    [Fact(Timeout = 5000)]
    public void Create_should_set_request_lifetime_feature()
    {
        var requestFeature = new TurboHttpRequestFeature();
        var ctx = FeatureCollectionFactory.Create(requestFeature, hasBody: false);

        var lifetime = ctx.Get<IHttpRequestLifetimeFeature>();
        Assert.NotNull(lifetime);
    }

    [Fact(Timeout = 5000)]
    public void RequestLifetimeFeature_should_support_abort()
    {
        var requestFeature = new TurboHttpRequestFeature();
        var ctx = FeatureCollectionFactory.Create(requestFeature, hasBody: false);

        var lifetime = ctx.Get<IHttpRequestLifetimeFeature>()!;
        lifetime.Abort();

        Assert.True(lifetime.RequestAborted.IsCancellationRequested);
    }

    [Fact(Timeout = 5000)]
    public void Create_should_set_request_identifier_feature()
    {
        var requestFeature = new TurboHttpRequestFeature();
        var ctx = FeatureCollectionFactory.Create(requestFeature, hasBody: false);

        var identifier = ctx.Get<IHttpRequestIdentifierFeature>();
        Assert.NotNull(identifier);
        Assert.NotEmpty(identifier.TraceIdentifier);
    }

    [Fact(Timeout = 5000)]
    public void RequestIdentifierFeature_should_support_custom_trace_id()
    {
        var requestFeature = new TurboHttpRequestFeature();
        var ctx = FeatureCollectionFactory.Create(requestFeature, hasBody: false);

        var identifier = ctx.Get<IHttpRequestIdentifierFeature>()!;
        identifier.TraceIdentifier = "custom-id";

        Assert.Equal("custom-id", identifier.TraceIdentifier);
    }

    [Fact(Timeout = 5000)]
    public void Create_should_set_reset_feature_as_null_for_http11()
    {
        var requestFeature = new TurboHttpRequestFeature();
        var ctx = FeatureCollectionFactory.Create(requestFeature, hasBody: false);

        var reset = ctx.Get<IHttpResetFeature>();
        Assert.Null(reset);
    }

    [Fact(Timeout = 5000)]
    public void Create_should_set_max_request_body_size_feature()
    {
        var requestFeature = new TurboHttpRequestFeature();
        var features =
            FeatureCollectionFactory.Create(requestFeature, hasBody: false, maxRequestBodySize: 10 * 1024 * 1024);

        var maxBodyFeature = features.Get<IHttpMaxRequestBodySizeFeature>();
        Assert.NotNull(maxBodyFeature);
        Assert.Equal(10 * 1024 * 1024, maxBodyFeature.MaxRequestBodySize);
        Assert.False(maxBodyFeature.IsReadOnly);
    }

    [Fact(Timeout = 5000)]
    public void Create_should_set_null_max_body_size_when_unlimited()
    {
        var requestFeature = new TurboHttpRequestFeature();
        var features = FeatureCollectionFactory.Create(requestFeature, hasBody: false, maxRequestBodySize: null);

        var maxBodyFeature = features.Get<IHttpMaxRequestBodySizeFeature>();
        Assert.NotNull(maxBodyFeature);
        Assert.Null(maxBodyFeature.MaxRequestBodySize);
    }

    [Fact(Timeout = 5000)]
    public void Create_should_set_body_control_feature_with_sync_io_disabled()
    {
        var requestFeature = new TurboHttpRequestFeature();
        var features = FeatureCollectionFactory.Create(requestFeature, hasBody: false);

        var bodyControl = features.Get<IHttpBodyControlFeature>();
        Assert.NotNull(bodyControl);
        Assert.False(bodyControl.AllowSynchronousIO);
    }
}