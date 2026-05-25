using Akka.Actor;
using Akka.Hosting;
using Akka.Streams.Dsl;
using Microsoft.Extensions.DependencyInjection;
using TurboHTTP.Server;
using TurboHTTP.Routing;
using TurboHTTP.Streams.Stages.Server;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Streams.Stages.Server;

public sealed class EntityDispatcherSpec : StreamTestBase
{
    private static readonly TurboRequestDelegate NoOpPipeline = _ => Task.CompletedTask;

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

    private TurboHttpContext CreateTestContext(
        HttpMethod method,
        string uri,
        IServiceProvider services)
    {
        var path = new Uri(uri).PathAndQuery;
        return ServerTestContext.Request()
            .Method(method.Method)
            .Path(path)
            .Services(services)
            .Materializer(Materializer)
            .Build();
    }

    private (RouteTable Table, IServiceProvider Services) SetupAskRoute(IActorRef actorRef)
    {
        var registry = new ActorRegistry();
        registry.Register<OrderActorKey>(actorRef);

        var services = new ServiceCollection()
            .AddSingleton(registry)
            .AddSingleton<IReadOnlyActorRegistry>(registry)
            .BuildServiceProvider();

        var turboTable = new TurboRouteTable();
        var builder = new TurboEntityBuilder("/orders/{id}");
        builder.OnGet((TurboHttpContext ctx) => new GetOrder(ctx.Request.RouteValues["id"]!.ToString()!));
        builder.UseActorRef<OrderActorKey>();
        builder.Response<OrderResult>((ctx, _) =>
        {
            ctx.Response.StatusCode = 200;
            return Task.CompletedTask;
        });
        builder.Response<OrderDeleted>((ctx, _) =>
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

        var stage = new RoutingStage(table, NoOpPipeline, 1, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5));
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
            .AddSingleton<IReadOnlyActorRegistry>(registry)
            .BuildServiceProvider();

        var turboTable = new TurboRouteTable();
        var builder = new TurboEntityBuilder("/orders/{id}");
        builder.OnPost((TurboHttpContext _) => new GetOrder("new")).Tell();
        builder.UseActorRef<OrderActorKey>();
        builder.AddToRouteTable(turboTable);

        var stage = new RoutingStage(turboTable.Freeze(), NoOpPipeline, 1, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5));
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
            .AddSingleton<IReadOnlyActorRegistry>(registry)
            .BuildServiceProvider();

        var turboTable = new TurboRouteTable();
        var builder = new TurboEntityBuilder("/orders/{id}");
        builder.OnGet((TurboHttpContext ctx) => new GetOrder(ctx.Request.RouteValues["id"]!.ToString()!));
        builder.UseActorRef<OrderActorKey>();
        builder.WithTimeout(TimeSpan.FromMilliseconds(100));
        builder.AddToRouteTable(turboTable);

        var stage = new RoutingStage(turboTable.Freeze(), NoOpPipeline, 1, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5));
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
            .AddSingleton<IReadOnlyActorRegistry>(registry)
            .BuildServiceProvider();

        var turboTable = new TurboRouteTable();
        var builder = new TurboEntityBuilder("/orders/{id}");
        builder.OnGet((TurboHttpContext ctx) => new GetOrder(ctx.Request.RouteValues["id"]!.ToString()!));
        builder.UseActorRef<OrderActorKey>();
        builder.AddToRouteTable(turboTable);

        var stage = new RoutingStage(turboTable.Freeze(), NoOpPipeline, 1, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5));
        var ctx = CreateTestContext(HttpMethod.Get, "http://localhost/orders/42", services);

        var result = await Source.Single(ctx)
            .Via(Flow.FromGraph(stage))
            .RunWith(Sink.First<TurboHttpContext>(), Materializer);

        Assert.Equal(500, result.Response.StatusCode);
    }
}
