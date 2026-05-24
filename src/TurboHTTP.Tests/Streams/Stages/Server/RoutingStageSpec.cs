using Akka.Streams.Dsl;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using TurboHTTP.Server;
using TurboHTTP.Server.Middleware;
using TurboHTTP.Routing;
using TurboHTTP.Streams.Stages.Server;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Streams.Stages.Server;

public sealed class RoutingStageSpec : StreamTestBase
{
    private static readonly TurboRequestDelegate NoOpPipeline = _ => Task.CompletedTask;

    private TurboHttpContext CreateTestContext(string method, string uri)
    {
        var path = new Uri(uri).PathAndQuery;
        return ServerTestContext.Request()
            .Method(method)
            .Path(path)
            .Services(new ServiceCollection().BuildServiceProvider())
            .Materializer(Materializer)
            .Build();
    }

    [Fact(Timeout = 5000)]
    public async Task Stage_should_route_request_to_matching_handler()
    {
        var routeTable = new RouteTableBuilder()
            .Add("GET", "/api/health",
                new DelegateDispatcher(ctx =>
                {
                    ctx.Response.StatusCode = 200;
                    return Task.CompletedTask;
                }))
            .Build();

        var stage = new RoutingStage(routeTable, NoOpPipeline, 1, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5));
        var ctx = CreateTestContext("GET", "http://localhost/api/health");

        var result = await Source.Single(ctx)
            .Via(Flow.FromGraph(stage))
            .RunWith(Sink.First<TurboHttpContext>(), Materializer);

        Assert.Equal(200, result.Response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public async Task Stage_should_return_404_for_unmatched_route()
    {
        var routeTable = new RouteTableBuilder().Build();
        var stage = new RoutingStage(routeTable, NoOpPipeline, 1, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5));
        var ctx = CreateTestContext("GET", "http://localhost/api/unknown");

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
            .Add("GET", "/api/orders/{id}", new DelegateDispatcher(ctx =>
            {
                capturedId = ctx.Request.RouteValues["id"]?.ToString();
                ctx.Response.StatusCode = 200;
                return Task.CompletedTask;
            }))
            .Build();

        var stage = new RoutingStage(routeTable, NoOpPipeline, 1, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5));
        var ctx = CreateTestContext("GET", "http://localhost/api/orders/42");

        await Source.Single(ctx)
            .Via(Flow.FromGraph(stage))
            .RunWith(Sink.First<TurboHttpContext>(), Materializer);

        Assert.Equal("42", capturedId);
    }

    [Fact(Timeout = 5000)]
    public async Task Stage_should_return_500_on_dispatch_failure()
    {
        var routeTable = new RouteTableBuilder()
            .Add("GET", "/api/fail",
                new DelegateDispatcher(_ => throw new InvalidOperationException("boom")))
            .Build();

        var stage = new RoutingStage(routeTable, NoOpPipeline, 1, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5));
        var ctx = CreateTestContext("GET", "http://localhost/api/fail");

        var result = await Source.Single(ctx)
            .Via(Flow.FromGraph(stage))
            .RunWith(Sink.First<TurboHttpContext>(), Materializer);

        Assert.Equal(500, result.Response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public async Task Stage_should_push_context_when_StartAsync_called_before_handler_completes()
    {
        var handlerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handlerRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var routeTable = new RouteTableBuilder()
            .Add("GET", "/api/stream",
                new DelegateDispatcher(async ctx =>
                {
                    ctx.Response.StatusCode = 200;
                    ctx.Response.ContentType = "text/event-stream";
                    await ctx.Features.Get<IHttpResponseBodyFeature>()!.StartAsync();
                    handlerStarted.SetResult();
                    await handlerRelease.Task;
                }))
            .Build();

        var stage = new RoutingStage(routeTable, NoOpPipeline, 1, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5));
        var ctx = CreateTestContext("GET", "http://localhost/api/stream");

        var resultTask = Source.Single(ctx)
            .Via(Flow.FromGraph(stage))
            .RunWith(Sink.First<TurboHttpContext>(), Materializer);

        await handlerStarted.Task;

        var result = await resultTask;
        Assert.Equal(200, result.Response.StatusCode);
        Assert.Equal("text/event-stream", result.Response.ContentType);

        handlerRelease.SetResult();
    }

    [Fact(Timeout = 5000)]
    public async Task Stage_should_still_push_after_handler_completes_without_StartAsync()
    {
        var routeTable = new RouteTableBuilder()
            .Add("GET", "/api/sync",
                new DelegateDispatcher(async ctx =>
                {
                    await Task.Delay(50);
                    ctx.Response.StatusCode = 201;
                }))
            .Build();

        var stage = new RoutingStage(routeTable, NoOpPipeline, 1, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5));
        var ctx = CreateTestContext("GET", "http://localhost/api/sync");

        var result = await Source.Single(ctx)
            .Via(Flow.FromGraph(stage))
            .RunWith(Sink.First<TurboHttpContext>(), Materializer);

        Assert.Equal(201, result.Response.StatusCode);
    }
}
