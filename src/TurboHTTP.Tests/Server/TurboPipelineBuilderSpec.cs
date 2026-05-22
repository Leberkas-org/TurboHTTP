using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using TurboHTTP.Server;
using TurboHTTP.Context.Features;
using TurboHTTP.Server.Middleware;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Server;

public sealed class TurboPipelineBuilderSpec
{
    private static TurboHttpContext CreateTestContext()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/test");
        var features = new FeatureCollection();
        features.Set<IHttpRequestFeature>(ServerTestContext.CreateRequestFeature(request));
        features.Set<IHttpResponseFeature>(new TurboHttpResponseFeature());

        return new TurboHttpContext(
            features,
            new TurboConnectionInfo("test", null, 0, null, 0),
            new ServiceCollection().BuildServiceProvider(),
            CancellationToken.None, null!);
    }

    [Fact(Timeout = 5000)]
    public async Task Build_should_return_noop_when_empty()
    {
        var builder = new TurboPipelineBuilder();
        var pipeline = builder.Build();

        var ctx = CreateTestContext();
        await pipeline(ctx);

        Assert.Equal(200, ctx.Response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public async Task Build_should_execute_middleware_in_order()
    {
        var order = new List<int>();
        var builder = new TurboPipelineBuilder();
        builder.Use((ctx, next) => { order.Add(1); return next(ctx); });
        builder.Use((ctx, next) => { order.Add(2); return next(ctx); });

        var pipeline = builder.Build();
        await pipeline(CreateTestContext());

        Assert.Equal([1, 2], order);
    }

    [Fact(Timeout = 5000)]
    public async Task Build_should_allow_Run_as_terminal()
    {
        var terminated = false;
        var builder = new TurboPipelineBuilder();
        builder.Run(_ => { terminated = true; return Task.CompletedTask; });

        var pipeline = builder.Build();
        await pipeline(CreateTestContext());

        Assert.True(terminated);
    }

    [Fact(Timeout = 5000)]
    public async Task Build_should_support_Map_branching()
    {
        var branched = false;
        var builder = new TurboPipelineBuilder();
        builder.Map("/admin", branch =>
        {
            branch.Use((ctx, next) => { branched = true; return next(ctx); });
        });

        var pipeline = builder.Build();

        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/admin/dashboard");
        var features = new FeatureCollection();
        features.Set<IHttpRequestFeature>(ServerTestContext.CreateRequestFeature(request));
        features.Set<IHttpResponseFeature>(new TurboHttpResponseFeature());

        var ctx = new TurboHttpContext(
            features,
            new TurboConnectionInfo("test", null, 0, null, 0),
            new ServiceCollection().BuildServiceProvider(),
            CancellationToken.None, null!);

        await pipeline(ctx);

        Assert.True(branched);
    }

    [Fact(Timeout = 5000)]
    public async Task Build_should_support_MapWhen_predicate()
    {
        var branched = false;
        var builder = new TurboPipelineBuilder();
        builder.MapWhen(_ => true, branch =>
        {
            branch.Use((ctx, next) => { branched = true; return next(ctx); });
        });

        var pipeline = builder.Build();
        await pipeline(CreateTestContext());

        Assert.True(branched);
    }
}
