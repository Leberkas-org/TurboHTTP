using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Context.Features;
using TurboHTTP.Server;

namespace TurboHTTP.Tests.Server;

public sealed class ServerContextFactorySpec
{
    [Fact(Timeout = 5000)]
    public void Create_should_set_request_feature()
    {
        var requestFeature = new TurboHttpRequestFeature { Method = "POST", Path = "/api" };
        var ctx = ServerContextFactory.Create(requestFeature, hasBody: false);

        Assert.Equal("POST", ctx.Request.Method);
        Assert.Equal("/api", ctx.Request.Path);
    }

    [Fact(Timeout = 5000)]
    public void Create_should_set_response_feature()
    {
        var requestFeature = new TurboHttpRequestFeature();
        var ctx = ServerContextFactory.Create(requestFeature, hasBody: false);

        var responseFeature = ctx.Features.Get<IHttpResponseFeature>();
        Assert.NotNull(responseFeature);
    }

    [Fact(Timeout = 5000)]
    public void Create_should_set_request_body_feature()
    {
        var requestFeature = new TurboHttpRequestFeature();
        var ctx = ServerContextFactory.Create(requestFeature, hasBody: false);

        var bodyFeature = ctx.Features.Get<ITurboRequestBodyFeature>();
        Assert.NotNull(bodyFeature);
    }

    [Fact(Timeout = 5000)]
    public void Create_should_set_body_detection_true_when_has_body()
    {
        var requestFeature = new TurboHttpRequestFeature();
        var ctx = ServerContextFactory.Create(requestFeature, hasBody: true);

        var detection = ctx.Features.Get<IHttpRequestBodyDetectionFeature>();
        Assert.NotNull(detection);
        Assert.True(detection.CanHaveBody);
    }

    [Fact(Timeout = 5000)]
    public void Create_should_set_body_detection_false_when_no_body()
    {
        var requestFeature = new TurboHttpRequestFeature();
        var ctx = ServerContextFactory.Create(requestFeature, hasBody: false);

        var detection = ctx.Features.Get<IHttpRequestBodyDetectionFeature>();
        Assert.NotNull(detection);
        Assert.False(detection.CanHaveBody);
    }

    [Fact(Timeout = 5000)]
    public void Create_should_set_response_body_feature()
    {
        var requestFeature = new TurboHttpRequestFeature();
        var ctx = ServerContextFactory.Create(requestFeature, hasBody: false);

        var responseBodyFeature = ctx.Features.Get<IHttpResponseBodyFeature>();
        Assert.NotNull(responseBodyFeature);

        var turboResponseBody = ctx.Features.Get<ITurboResponseBodyFeature>();
        Assert.NotNull(turboResponseBody);
    }

    [Fact(Timeout = 5000)]
    public void Create_should_set_request_lifetime_feature()
    {
        var requestFeature = new TurboHttpRequestFeature();
        var ctx = ServerContextFactory.Create(requestFeature, hasBody: false);

        var lifetime = ctx.Features.Get<IHttpRequestLifetimeFeature>();
        Assert.NotNull(lifetime);
        Assert.Equal(ctx.RequestAborted, lifetime.RequestAborted);
    }

    [Fact(Timeout = 5000)]
    public void RequestLifetimeFeature_should_delegate_abort_to_context()
    {
        var requestFeature = new TurboHttpRequestFeature();
        var ctx = ServerContextFactory.Create(requestFeature, hasBody: false);

        var lifetime = ctx.Features.Get<IHttpRequestLifetimeFeature>()!;
        lifetime.Abort();

        Assert.True(ctx.RequestAborted.IsCancellationRequested);
    }

    [Fact(Timeout = 5000)]
    public void Create_should_set_request_identifier_feature()
    {
        var requestFeature = new TurboHttpRequestFeature();
        var ctx = ServerContextFactory.Create(requestFeature, hasBody: false);

        var identifier = ctx.Features.Get<IHttpRequestIdentifierFeature>();
        Assert.NotNull(identifier);
        Assert.Equal(ctx.TraceIdentifier, identifier.TraceIdentifier);
    }

    [Fact(Timeout = 5000)]
    public void RequestIdentifierFeature_should_sync_with_context()
    {
        var requestFeature = new TurboHttpRequestFeature();
        var ctx = ServerContextFactory.Create(requestFeature, hasBody: false);

        var identifier = ctx.Features.Get<IHttpRequestIdentifierFeature>()!;
        identifier.TraceIdentifier = "custom-id";

        Assert.Equal("custom-id", ctx.TraceIdentifier);
    }

    [Fact(Timeout = 5000)]
    public void Create_should_set_reset_feature_as_null_for_http11()
    {
        var requestFeature = new TurboHttpRequestFeature();
        var ctx = ServerContextFactory.Create(requestFeature, hasBody: false);

        var reset = ctx.Features.Get<IHttpResetFeature>();
        Assert.Null(reset);
    }
}
