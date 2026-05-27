using Akka.Actor;
using Akka.TestKit.Xunit;
using Microsoft.AspNetCore.Http;

namespace Servus.Akka.AspNetCore.Tests;

public sealed class EntityDispatcherSpec : TestKit
{
    public EntityDispatcherSpec() : base(ActorSystem.Create("test"))
    {
    }

    private sealed record TestMessage(string Value);

    private sealed record TestResponse(string Result);

    private sealed class EchoActor : ReceiveActor
    {
        public EchoActor()
        {
            Receive<TestMessage>(msg => Sender.Tell(new TestResponse(msg.Value)));
        }
    }

    private sealed class TestResolver(IActorRef actor) : IEntityActorResolver
    {
        public ValueTask<IActorRef> ResolveAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(actor);
        }
    }

    private static DefaultHttpContext CreateTestContext()
    {
        var ctx = new DefaultHttpContext
        {
            Response =
            {
                Body = new MemoryStream()
            }
        };
        return ctx;
    }

    [Fact(Timeout = 5000)]
    public async Task Ask_should_dispatch_message_and_map_response()
    {
        var actor = Sys.ActorOf(Props.Create<EchoActor>());
        var responseMappers = new EntityResponseMapperCollection();
        responseMappers.Add<TestResponse>(async (ctx, resp) =>
        {
            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsync(resp.Result);
        });

        var config = new EntityMethodConfig(
            MessageFactory: (Func<TestMessage>)(() => new TestMessage("hello")),
            IsTell: false,
            TimeoutOverride: null,
            EndpointMappers: responseMappers,
            TellResponseHandler: null);

        var dispatcher = new EntityDispatcher(config, new EntityResponseMapperCollection(),
            TimeSpan.FromSeconds(5), new TestResolver(actor));

        var ctx = CreateTestContext();
        await dispatcher.DispatchAsync(ctx, new TestMessage("hello"));

        Assert.Equal(200, ctx.Response.StatusCode);
        ctx.Response.Body.Position = 0;
        using var reader = new StreamReader(ctx.Response.Body);
        var body = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
        Assert.Equal("hello", body);
    }

    [Fact(Timeout = 5000)]
    public async Task Tell_should_send_message_and_return_202()
    {
        var probe = CreateTestProbe();
        var config = new EntityMethodConfig(
            MessageFactory: (Func<TestMessage>)(() => new TestMessage("fire")),
            IsTell: true,
            TimeoutOverride: null,
            EndpointMappers: null,
            TellResponseHandler: null);

        var dispatcher = new EntityDispatcher(config, new EntityResponseMapperCollection(),
            TimeSpan.FromSeconds(5), new TestResolver(probe.Ref));

        var ctx = CreateTestContext();
        await dispatcher.DispatchAsync(ctx, new TestMessage("fire"));

        Assert.Equal(202, ctx.Response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public async Task Tell_should_use_custom_status_code()
    {
        var probe = CreateTestProbe();
        var config = new EntityMethodConfig(
            MessageFactory: (Func<TestMessage>)(() => new TestMessage("fire")),
            IsTell: true,
            TimeoutOverride: null,
            EndpointMappers: null,
            TellResponseHandler: ctx =>
            {
                ctx.Response.StatusCode = 204;
                return Task.CompletedTask;
            });

        var dispatcher = new EntityDispatcher(config, new EntityResponseMapperCollection(),
            TimeSpan.FromSeconds(5), new TestResolver(probe.Ref));

        var ctx = CreateTestContext();
        await dispatcher.DispatchAsync(ctx, new TestMessage("fire"));

        Assert.Equal(204, ctx.Response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public async Task Ask_should_return_504_on_timeout()
    {
        var blackhole = Sys.ActorOf(Props.Create(() => new BlackholeActor()));
        var config = new EntityMethodConfig(
            MessageFactory: (Func<TestMessage>)(() => new TestMessage("timeout")),
            IsTell: false,
            TimeoutOverride: TimeSpan.FromMilliseconds(100),
            EndpointMappers: new EntityResponseMapperCollection(),
            TellResponseHandler: null);

        var dispatcher = new EntityDispatcher(config, new EntityResponseMapperCollection(),
            TimeSpan.FromSeconds(5), new TestResolver(blackhole));

        var ctx = CreateTestContext();
        await dispatcher.DispatchAsync(ctx, new TestMessage("timeout"));

        Assert.Equal(504, ctx.Response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public async Task Ask_should_return_500_when_no_mapper_found()
    {
        var actor = Sys.ActorOf(Props.Create<EchoActor>());
        var config = new EntityMethodConfig(
            MessageFactory: (Func<TestMessage>)(() => new TestMessage("hello")),
            IsTell: false,
            TimeoutOverride: null,
            EndpointMappers: new EntityResponseMapperCollection(),
            TellResponseHandler: null);

        var dispatcher = new EntityDispatcher(config, new EntityResponseMapperCollection(),
            TimeSpan.FromSeconds(5), new TestResolver(actor));

        var ctx = CreateTestContext();
        await dispatcher.DispatchAsync(ctx, new TestMessage("hello"));

        Assert.Equal(500, ctx.Response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public async Task Ask_should_use_endpoint_mappers_first()
    {
        var actor = Sys.ActorOf(Props.Create<EchoActor>());
        var endpointMappers = new EntityResponseMapperCollection();
        var globalMappers = new EntityResponseMapperCollection();

        endpointMappers.Add<TestResponse>(async (ctx, _) =>
        {
            ctx.Response.StatusCode = 201;
            await ctx.Response.WriteAsync("endpoint");
        });

        globalMappers.Add<TestResponse>(async (ctx, _) =>
        {
            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsync("global");
        });

        var config = new EntityMethodConfig(
            MessageFactory: (Func<TestMessage>)(() => new TestMessage("hello")),
            IsTell: false,
            TimeoutOverride: null,
            EndpointMappers: endpointMappers,
            TellResponseHandler: null);

        var dispatcher = new EntityDispatcher(config, globalMappers,
            TimeSpan.FromSeconds(5), new TestResolver(actor));

        var ctx = CreateTestContext();
        await dispatcher.DispatchAsync(ctx, new TestMessage("hello"));

        Assert.Equal(201, ctx.Response.StatusCode);
        ctx.Response.Body.Position = 0;
        using var reader = new StreamReader(ctx.Response.Body);
        var body = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
        Assert.Equal("endpoint", body);
    }

    [Fact(Timeout = 5000)]
    public async Task Ask_should_use_global_mappers_as_fallback()
    {
        var actor = Sys.ActorOf(Props.Create<EchoActor>());
        var globalMappers = new EntityResponseMapperCollection();

        globalMappers.Add<TestResponse>(async (ctx, resp) =>
        {
            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsync(resp.Result);
        });

        var config = new EntityMethodConfig(
            MessageFactory: (Func<TestMessage>)(() => new TestMessage("hello")),
            IsTell: false,
            TimeoutOverride: null,
            EndpointMappers: null,
            TellResponseHandler: null);

        var dispatcher = new EntityDispatcher(config, globalMappers,
            TimeSpan.FromSeconds(5), new TestResolver(actor));

        var ctx = CreateTestContext();
        await dispatcher.DispatchAsync(ctx, new TestMessage("hello"));

        Assert.Equal(200, ctx.Response.StatusCode);
        ctx.Response.Body.Position = 0;
        using var reader = new StreamReader(ctx.Response.Body);
        var body = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
        Assert.Equal("hello", body);
    }

    [Fact(Timeout = 5000)]
    public async Task Ask_should_return_504_when_actor_throws_exception()
    {
        var thrower = Sys.ActorOf(Props.Create(() => new ThrowingActor()));
        var config = new EntityMethodConfig(
            MessageFactory: (Func<TestMessage>)(() => new TestMessage("error")),
            IsTell: false,
            TimeoutOverride: TimeSpan.FromMilliseconds(100),
            EndpointMappers: new EntityResponseMapperCollection(),
            TellResponseHandler: null);

        var dispatcher = new EntityDispatcher(config, new EntityResponseMapperCollection(),
            TimeSpan.FromSeconds(5), new TestResolver(thrower));

        var ctx = CreateTestContext();
        await dispatcher.DispatchAsync(ctx, new TestMessage("error"));

        Assert.Equal(504, ctx.Response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public async Task Tell_should_return_202_when_resolver_throws()
    {
        var failingResolver = new FailingResolver();
        var config = new EntityMethodConfig(
            MessageFactory: (Func<TestMessage>)(() => new TestMessage("error")),
            IsTell: true,
            TimeoutOverride: null,
            EndpointMappers: null,
            TellResponseHandler: null);

        var dispatcher = new EntityDispatcher(config, new EntityResponseMapperCollection(),
            TimeSpan.FromSeconds(5), failingResolver);

        var ctx = CreateTestContext();
        await dispatcher.DispatchAsync(ctx, new TestMessage("error"));

        Assert.Equal(503, ctx.Response.StatusCode);
    }

    private sealed class BlackholeActor : ReceiveActor
    {
        public BlackholeActor()
        {
            ReceiveAny(_ => { });
        }
    }

    private sealed class ThrowingActor : ReceiveActor
    {
        public ThrowingActor()
        {
            ReceiveAny(_ => throw new InvalidOperationException("Test error"));
        }
    }

    private sealed class FailingResolver : IEntityActorResolver
    {
        public ValueTask<IActorRef> ResolveAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Resolution failed");
        }
    }
}