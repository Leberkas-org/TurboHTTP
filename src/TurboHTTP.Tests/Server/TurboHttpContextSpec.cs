using System.Net;
using Akka.Streams.Dsl;
using TurboHTTP.Server;

namespace TurboHTTP.Tests.Server;

public sealed class TurboHttpContextSpec
{
    [Fact(Timeout = 5000)]
    public void TurboHttpContext_should_expose_request()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/test");
        var ctx = CreateContext(request);
        Assert.Same(request, ctx.Request);
    }

    [Fact(Timeout = 5000)]
    public void TurboHttpContext_should_expose_mutable_response()
    {
        var ctx = CreateContext();
        var response = new HttpResponseMessage(HttpStatusCode.NotFound);
        ctx.Response = response;
        Assert.Same(response, ctx.Response);
    }

    [Fact(Timeout = 5000)]
    public void TurboHttpContext_should_expose_request_body_source()
    {
        var ctx = CreateContext();
        Assert.NotNull(ctx.RequestBodySource);
    }

    [Fact(Timeout = 5000)]
    public void TurboHttpContext_should_expose_mutable_response_body_source()
    {
        var ctx = CreateContext();
        var source = Source.Single(new ReadOnlyMemory<byte>([1, 2, 3]));
        ctx.ResponseBodySource = source;
        Assert.Same(source, ctx.ResponseBodySource);
    }

    [Fact(Timeout = 5000)]
    public void TurboHttpContext_should_expose_connection_info()
    {
        var connection = new TurboConnectionInfo("conn-1", IPAddress.Loopback, 12345, IPAddress.Loopback, 443);
        var ctx = CreateContext(connection: connection);
        Assert.Equal("conn-1", ctx.Connection.Id);
    }

    [Fact(Timeout = 5000)]
    public void TurboHttpContext_should_expose_empty_items_by_default()
    {
        var ctx = CreateContext();
        Assert.NotNull(ctx.Items);
        Assert.Empty(ctx.Items);
    }

    [Fact(Timeout = 5000)]
    public void TurboHttpContext_should_expose_route_values()
    {
        var ctx = CreateContext();
        Assert.NotNull(ctx.RouteValues);
    }

    private static TurboHttpContext CreateContext(
        HttpRequestMessage? request = null,
        TurboConnectionInfo? connection = null)
    {
        return new TurboHttpContext(
            request ?? new HttpRequestMessage(HttpMethod.Get, "http://localhost/"),
            connection ?? new TurboConnectionInfo("test", IPAddress.Loopback, 0, IPAddress.Loopback, 0),
            Source.Empty<ReadOnlyMemory<byte>>(),
            CancellationToken.None);
    }
}
