using Akka.Actor;
using Akka.Hosting;
using Akka.Streams.Dsl;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using TurboHTTP.Server;
using TurboHTTP.Server.Context.Features;
using TurboHTTP.Routing;
using TurboHTTP.Streams.Stages.Server;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Streams.Stages.Server;

public sealed class EntityDispatcherSpec : StreamTestBase
{
    private sealed class OrderActorKey;

    private sealed record GetOrder(string Id);

    private sealed record OrderResult(string Id, string Name);

    private sealed record DeleteOrder(string Id);

    private sealed record OrderDeleted;

    private sealed class OrderActor : ReceiveActor
    {
        public OrderActor()
        {
            Receive<GetOrder>(msg => Sender.Tell(new OrderResult(msg.Id, "Widget")));
            Receive<DeleteOrder>(_ => Sender.Tell(new OrderDeleted()));
        }
    }

    private static TurboHttpContext CreateTestContext(
        HttpMethod method,
        string uri,
        IServiceProvider services)
    {
        var request = new HttpRequestMessage(method, uri);

        var features = new FeatureCollection();
        var requestFeature = new TurboHttpRequestFeature(request, Source.Empty<ReadOnlyMemory<byte>>());
        features.Set<IHttpRequestFeature>(requestFeature);
        features.Set<ITurboRequestBodyFeature>(requestFeature);
        features.Set<IHttpResponseFeature>(new TurboHttpResponseFeature());
        var bodyFeature = new TurboHttpResponseBodyFeature();
        features.Set<IHttpResponseBodyFeature>(bodyFeature);
        features.Set<ITurboResponseBodyFeature>(bodyFeature);

        return new TurboHttpContext(
            features,
            new TurboConnectionInfo("test", null, 0, null, 0),
            services,
            CancellationToken.None);
    }

    private (RouteTable Table, IServiceProvider Services) SetupAskRoute(IActorRef actorRef)
    {
        var registry = new ActorRegistry();
        registry.Register<OrderActorKey>(actorRef);

        var services = new ServiceCollection()
            .AddSingleton(registry)
            .BuildServiceProvider();

        var turboTable = new TurboRouteTable();
        var builder = new TurboEntityBuilder("/orders/{id}");
        builder.OnGet((TurboHttpContext ctx) => new GetOrder(ctx.Request.RouteValues["id"]!.ToString()!));
        builder.UseActorRef<OrderActorKey>();
        builder.MapResponse<OrderResult>((ctx, _) =>
        {
            ctx.Response.StatusCode = 200;
            return Task.CompletedTask;
        });
        builder.MapResponse<OrderDeleted>((ctx, _) =>
        {
            ctx.Response.StatusCode = 204;
            return Task.CompletedTask;
        });
        builder.AddToRouteTable(turboTable);

        return (turboTable.Freeze(), services);
    }

    [Fact(Timeout = 5000)]
    public async Task Stage_should_dispatch_ask_to_actor_and_return_mapped_response()
    {
        var actor = Sys.ActorOf(Props.Create(() => new OrderActor()));
        var (table, services) = SetupAskRoute(actor);

        var stage = new RoutingStage(table);
        var ctx = CreateTestContext(HttpMethod.Get, "http://localhost/orders/42", services);

        var result = await Source.Single(ctx)
            .Via(Flow.FromGraph(stage))
            .RunWith(Sink.First<TurboHttpContext>(), Materializer);

        Assert.Equal(200, result.Response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public async Task Stage_should_return_202_for_tell_route()
    {
        var probe = CreateTestProbe();
        var registry = new ActorRegistry();
        registry.Register<OrderActorKey>(probe.Ref);

        var services = new ServiceCollection()
            .AddSingleton(registry)
            .BuildServiceProvider();

        var turboTable = new TurboRouteTable();
        var builder = new TurboEntityBuilder("/orders/{id}");
        builder.OnPost((TurboHttpContext _) => new GetOrder("new")).AcceptedResponse();
        builder.UseActorRef<OrderActorKey>();
        builder.AddToRouteTable(turboTable);

        var stage = new RoutingStage(turboTable.Freeze());
        var ctx = CreateTestContext(HttpMethod.Post, "http://localhost/orders/1", services);

        var result = await Source.Single(ctx)
            .Via(Flow.FromGraph(stage))
            .RunWith(Sink.First<TurboHttpContext>(), Materializer);

        Assert.Equal(202, result.Response.StatusCode);
        probe.ExpectMsg<GetOrder>(cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 5000)]
    public async Task Stage_should_return_504_on_ask_timeout()
    {
        var probe = CreateTestProbe();
        var registry = new ActorRegistry();
        registry.Register<OrderActorKey>(probe.Ref);

        var services = new ServiceCollection()
            .AddSingleton(registry)
            .BuildServiceProvider();

        var turboTable = new TurboRouteTable();
        var builder = new TurboEntityBuilder("/orders/{id}");
        builder.OnGet((TurboHttpContext ctx) => new GetOrder(ctx.Request.RouteValues["id"]!.ToString()!));
        builder.UseActorRef<OrderActorKey>();
        builder.WithTimeout(TimeSpan.FromMilliseconds(100));
        builder.AddToRouteTable(turboTable);

        var stage = new RoutingStage(turboTable.Freeze());
        var ctx = CreateTestContext(HttpMethod.Get, "http://localhost/orders/42", services);

        var result = await Source.Single(ctx)
            .Via(Flow.FromGraph(stage))
            .RunWith(Sink.First<TurboHttpContext>(), Materializer);

        Assert.Equal(504, result.Response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public async Task Stage_should_return_500_when_no_response_mapper_found()
    {
        var actor = Sys.ActorOf(Props.Create(() => new OrderActor()));
        var registry = new ActorRegistry();
        registry.Register<OrderActorKey>(actor);

        var services = new ServiceCollection()
            .AddSingleton(registry)
            .BuildServiceProvider();

        var turboTable = new TurboRouteTable();
        var builder = new TurboEntityBuilder("/orders/{id}");
        builder.OnGet((TurboHttpContext ctx) => new GetOrder(ctx.Request.RouteValues["id"]!.ToString()!));
        builder.UseActorRef<OrderActorKey>();
        builder.AddToRouteTable(turboTable);

        var stage = new RoutingStage(turboTable.Freeze());
        var ctx = CreateTestContext(HttpMethod.Get, "http://localhost/orders/42", services);

        var result = await Source.Single(ctx)
            .Via(Flow.FromGraph(stage))
            .RunWith(Sink.First<TurboHttpContext>(), Materializer);

        Assert.Equal(500, result.Response.StatusCode);
    }
}