using Akka;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.TestKit.Xunit;
using Microsoft.Extensions.DependencyInjection;
using Servus.Akka.Transport;
using TurboHTTP.Server;
using TurboHTTP.Streams.Lifecycle;

namespace TurboHTTP.Tests.Server.Lifecycle;

public sealed class ListenerActorConnectionLimitSpec : TestKit
{
    private sealed class FakeListenerFactory : IListenerFactory
    {
        private readonly Source<Flow<ITransportOutbound, ITransportInbound, NotUsed>, NotUsed> _source;

        public FakeListenerFactory(Source<Flow<ITransportOutbound, ITransportInbound, NotUsed>, NotUsed> source)
        {
            _source = source;
        }

        public Source<Flow<ITransportOutbound, ITransportInbound, NotUsed>, NotUsed> Bind(ListenerOptions options)
        {
            return _source;
        }
    }

    private sealed class ParentForListener : ReceiveActor
    {
        private IActorRef? _testActor;

        public ParentForListener()
        {
            Receive<CreateListenerWithLimit>(msg =>
            {
                _testActor = Sender;

                var factory = new FakeListenerFactory(
                    Source.Empty<Flow<ITransportOutbound, ITransportInbound, NotUsed>>());
                var serverOptions = new TurboServerOptions
                {
                    MaxConcurrentConnections = msg.MaxConcurrentConnections
                };
                TurboRequestDelegate pipeline = _ => Task.CompletedTask;
                var routeTable = new TurboHTTP.Routing.RouteTable([]);
                var services = new ServiceCollection().BuildServiceProvider();
                var materializer = Context.System.Materializer();

                var listenerActor = Context.ActorOf(
                    ListenerActor.Create(
                        factory,
                        msg.ListenerOptions,
                        serverOptions,
                        pipeline,
                        routeTable,
                        services,
                        materializer),
                    "listener");

                Context.Watch(listenerActor);
                _testActor.Tell(listenerActor, ActorRefs.NoSender);
            });

            Receive<ListenerActor.ConnectionStarted>(msg =>
            {
                _testActor?.Tell(msg, ActorRefs.NoSender);
            });
        }

        public sealed record CreateListenerWithLimit(
            ListenerOptions ListenerOptions,
            int MaxConcurrentConnections);
    }

    private static Flow<ITransportOutbound, ITransportInbound, NotUsed> CreateDummyConnectionFlow()
    {
        return Flow.Create<ITransportOutbound>()
            .Select(_ => (ITransportInbound)new TransportData(TransportBuffer.Rent(1)));
    }

    [Fact(Timeout = 5000)]
    public void ListenerActor_should_accept_connections_when_limit_is_zero()
    {
        var listenerOptions = new TcpListenerOptions { Host = "localhost", Port = 8080 };

        var parentActor = Sys.ActorOf(Props.Create(() => new ParentForListener()), "parent-unlimited");

        parentActor.Tell(
            new ParentForListener.CreateListenerWithLimit(
                listenerOptions,
                MaxConcurrentConnections: 0),
            TestActor);

        var listenerActor = ExpectMsg<IActorRef>(cancellationToken: TestContext.Current.CancellationToken);

        // Send multiple connections
        for (int i = 0; i < 5; i++)
        {
            var dummyFlow = CreateDummyConnectionFlow();
            listenerActor.Tell(new ListenerActor.IncomingConnection(dummyFlow), ActorRefs.NoSender);
        }

        // All connections should be accepted (ConnectionStarted messages received)
        for (int i = 0; i < 5; i++)
        {
            ExpectMsg<ListenerActor.ConnectionStarted>(
                cancellationToken: TestContext.Current.CancellationToken);
        }
    }

    [Fact(Timeout = 5000)]
    public void ListenerActor_should_reject_connections_when_limit_reached()
    {
        var listenerOptions = new TcpListenerOptions { Host = "localhost", Port = 8080 };

        var parentActor = Sys.ActorOf(Props.Create(() => new ParentForListener()), "parent-limited");

        parentActor.Tell(
            new ParentForListener.CreateListenerWithLimit(
                listenerOptions,
                MaxConcurrentConnections: 2),
            TestActor);

        var listenerActor = ExpectMsg<IActorRef>(cancellationToken: TestContext.Current.CancellationToken);

        // Send 2 connections - should be accepted
        for (int i = 0; i < 2; i++)
        {
            var dummyFlow = CreateDummyConnectionFlow();
            listenerActor.Tell(new ListenerActor.IncomingConnection(dummyFlow), ActorRefs.NoSender);
        }

        // Receive the 2 acceptance messages
        for (int i = 0; i < 2; i++)
        {
            ExpectMsg<ListenerActor.ConnectionStarted>(
                cancellationToken: TestContext.Current.CancellationToken);
        }

        // Send a 3rd connection - should be rejected
        var rejectedFlow = CreateDummyConnectionFlow();
        listenerActor.Tell(new ListenerActor.IncomingConnection(rejectedFlow), ActorRefs.NoSender);

        // No ConnectionStarted message should arrive for the rejected connection
        ExpectNoMsg(TimeSpan.FromMilliseconds(500), cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 5000)]
    public void ListenerActor_should_not_accept_when_at_limit()
    {
        var listenerOptions = new TcpListenerOptions { Host = "localhost", Port = 8080 };

        var parentActor = Sys.ActorOf(Props.Create(() => new ParentForListener()), "parent-at-limit");

        parentActor.Tell(
            new ParentForListener.CreateListenerWithLimit(
                listenerOptions,
                MaxConcurrentConnections: 1),
            TestActor);

        var listenerActor = ExpectMsg<IActorRef>(cancellationToken: TestContext.Current.CancellationToken);

        // Send first connection - should be accepted
        var flow1 = CreateDummyConnectionFlow();
        listenerActor.Tell(new ListenerActor.IncomingConnection(flow1), ActorRefs.NoSender);

        var started1 = ExpectMsg<ListenerActor.ConnectionStarted>(
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(started1);

        // Send second connection - should be rejected
        var flow2 = CreateDummyConnectionFlow();
        listenerActor.Tell(new ListenerActor.IncomingConnection(flow2), ActorRefs.NoSender);

        // Verify no ConnectionStarted is sent for the rejected connection
        ExpectNoMsg(TimeSpan.FromMilliseconds(500), cancellationToken: TestContext.Current.CancellationToken);
    }
}
