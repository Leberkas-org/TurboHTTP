using Akka.Streams.Dsl;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using TurboHTTP.Server;
using TurboHTTP.Context.Features;
using TurboHTTP.Routing;
using TurboHTTP.Streams.Stages.Server;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Streams.Stages.Server;

public sealed class RoutingStageSpec : StreamTestBase
{
    private TurboHttpContext CreateTestContext(HttpMethod method, string uri)
    {
        var request = new HttpRequestMessage(method, uri);

        var features = new FeatureCollection();
        var requestFeature = ServerTestContext.CreateRequestFeature(request);
        features.Set<IHttpRequestFeature>(requestFeature);
        features.Set<ITurboRequestBodyFeature>(requestFeature);
        features.Set<IHttpResponseFeature>(new TurboHttpResponseFeature());
        var bodyFeature = new TurboHttpResponseBodyFeature();
        features.Set<IHttpResponseBodyFeature>(bodyFeature);
        features.Set<ITurboResponseBodyFeature>(bodyFeature);

        return new TurboHttpContext(
            features,
            new TurboConnectionInfo("test", null, 0, null, 0),
            new ServiceCollection().BuildServiceProvider(),
            CancellationToken.None, Materializer);
    }

    [Fact(Timeout = 5000)]
    public async Task Stage_should_route_request_to_matching_handler()
    {
        var routeTable = new RouteTableBuilder()
            .Add(HttpMethod.Get, "/api/health",
                new DelegateDispatcher(ctx =>
                {
                    ctx.Response.StatusCode = 200;
                    return Task.CompletedTask;
                }))
            .Build();

        var stage = new RoutingStage(routeTable);
        var ctx = CreateTestContext(HttpMethod.Get, "http://localhost/api/health");

        var result = await Source.Single(ctx)
            .Via(Flow.FromGraph(stage))
            .RunWith(Sink.First<TurboHttpContext>(), Materializer);

        Assert.Equal(200, result.Response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public async Task Stage_should_return_404_for_unmatched_route()
    {
        var routeTable = new RouteTableBuilder().Build();
        var stage = new RoutingStage(routeTable);
        var ctx = CreateTestContext(HttpMethod.Get, "http://localhost/api/unknown");

        var result = await Source.Single(ctx)
            .Via(Flow.FromGraph(stage))
            .RunWith(Sink.First<TurboHttpContext>(), Materializer);

        Assert.Equal(404, result.Response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public async Task Stage_should_populate_route_values()
    {
        string? capturedId = null;
        var routeTable = new RouteTableBuilder()
            .Add(HttpMethod.Get, "/api/orders/{id}", new DelegateDispatcher(ctx =>
            {
                capturedId = ctx.Request.RouteValues["id"]?.ToString();
                ctx.Response.StatusCode = 200;
                return Task.CompletedTask;
            }))
            .Build();

        var stage = new RoutingStage(routeTable);
        var ctx = CreateTestContext(HttpMethod.Get, "http://localhost/api/orders/42");

        await Source.Single(ctx)
            .Via(Flow.FromGraph(stage))
            .RunWith(Sink.First<TurboHttpContext>(), Materializer);

        Assert.Equal("42", capturedId);
    }

    [Fact(Timeout = 5000)]
    public async Task Stage_should_return_500_on_dispatch_failure()
    {
        var routeTable = new RouteTableBuilder()
            .Add(HttpMethod.Get, "/api/fail",
                new DelegateDispatcher(_ => throw new InvalidOperationException("boom")))
            .Build();

        var stage = new RoutingStage(routeTable);
        var ctx = CreateTestContext(HttpMethod.Get, "http://localhost/api/fail");

        var result = await Source.Single(ctx)
            .Via(Flow.FromGraph(stage))
            .RunWith(Sink.First<TurboHttpContext>(), Materializer);

        Assert.Equal(500, result.Response.StatusCode);
    }
}
