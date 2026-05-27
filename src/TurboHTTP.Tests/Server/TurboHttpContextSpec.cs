using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using TurboHTTP.Server;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Server;

public sealed class TurboHttpContextSpec
{
    [Fact(Timeout = 5000)]
    public void TurboHttpContext_should_be_instantiable_with_features()
    {
        var ctx = CreateContext();
        Assert.NotNull(ctx);
        Assert.NotNull(ctx.Features);
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
        var ctx = ServerTestContext.Request()
            .Get("/")
            .Connection(connection)
            .Build();
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
        var ctx = ServerTestContext.Request()
            .Get("/")
            .Services(services)
            .RequestAborted(TestContext.Current.CancellationToken)
            .Build();
        Assert.Same(services, ctx.RequestServices);
    }

    private static TurboHttpContext CreateContext()
    {
        return ServerTestContext.Request()
            .Get("/")
            .Connection(new TurboConnectionInfo("test", IPAddress.Loopback, 0, IPAddress.Loopback, 0))
            .RequestAborted(TestContext.Current.CancellationToken)
            .Build();
    }
}
