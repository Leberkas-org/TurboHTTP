using System.Net;
using Akka.Streams.Dsl;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using TurboHTTP.Server;
using TurboHTTP.Server.Context.Features;

namespace TurboHTTP.Tests.Server;

public sealed class TurboHttpContextSpec
{
    [Fact(Timeout = 5000)]
    public void TurboHttpContext_should_be_assignable_to_HttpContext()
    {
        var ctx = CreateContext();
        HttpContext baseRef = ctx;
        Assert.NotNull(baseRef);
    }

    [Fact(Timeout = 5000)]
    public void TurboHttpContext_should_expose_request_as_HttpRequest()
    {
        var ctx = CreateContext();
        Assert.NotNull(ctx.Request);
        Assert.Equal("GET", ctx.Request.Method);
    }

    [Fact(Timeout = 5000)]
    public void TurboHttpContext_should_expose_response_as_HttpResponse()
    {
        var ctx = CreateContext();
        Assert.NotNull(ctx.Response);
        Assert.Equal(200, ctx.Response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public void TurboHttpContext_should_expose_connection_as_ConnectionInfo()
    {
        var connection = new TurboConnectionInfo("conn-1", IPAddress.Loopback, 12345, IPAddress.Loopback, 443);
        var ctx = CreateContext(connection: connection);
        Assert.Equal("conn-1", ctx.Connection.Id);
        Assert.IsType<TurboConnectionInfo>(ctx.Connection);
    }

    [Fact(Timeout = 5000)]
    public void TurboHttpContext_should_expose_features()
    {
        var ctx = CreateContext();
        Assert.NotNull(ctx.Features);
        Assert.NotNull(ctx.Features.Get<IHttpRequestFeature>());
        Assert.NotNull(ctx.Features.Get<IHttpResponseFeature>());
        Assert.NotNull(ctx.Features.Get<IHttpConnectionFeature>());
    }

    [Fact(Timeout = 5000)]
    public void TurboHttpContext_should_expose_empty_items()
    {
        var ctx = CreateContext();
        Assert.NotNull(ctx.Items);
        Assert.Empty(ctx.Items);
    }

    [Fact(Timeout = 5000)]
    public void TurboHttpContext_should_expose_trace_identifier()
    {
        var ctx = CreateContext();
        Assert.NotNull(ctx.TraceIdentifier);
        Assert.NotEmpty(ctx.TraceIdentifier);
    }

    [Fact(Timeout = 5000)]
    public void TurboHttpContext_should_expose_request_aborted()
    {
        var ctx = CreateContext();
        Assert.Equal(TestContext.Current.CancellationToken, ctx.RequestAborted);
    }

    [Fact(Timeout = 5000)]
    public void TurboHttpContext_should_expose_request_services()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var ctx = CreateContext(services: services);
        Assert.Same(services, ctx.RequestServices);
    }

    private static TurboHttpContext CreateContext(
        HttpRequestMessage? request = null,
        TurboConnectionInfo? connection = null,
        IServiceProvider? services = null)
    {
        var req = request ?? new HttpRequestMessage(HttpMethod.Get, "http://localhost/");
        var conn = connection ?? new TurboConnectionInfo("test", IPAddress.Loopback, 0, IPAddress.Loopback, 0);

        var features = new FeatureCollection();
        features.Set<IHttpRequestFeature>(new TurboHttpRequestFeature(req, Source.Empty<ReadOnlyMemory<byte>>()));
        features.Set<IHttpResponseFeature>(new TurboHttpResponseFeature());
        features.Set<IHttpConnectionFeature>(new TurboHttpConnectionFeature(conn));
        features.Set<IHttpResponseBodyFeature>(new TurboHttpResponseBodyFeature());

        return new TurboHttpContext(features, conn, services, TestContext.Current.CancellationToken);
    }
}