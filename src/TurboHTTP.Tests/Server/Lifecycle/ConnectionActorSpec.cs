using Akka.Actor;
using Akka.TestKit.Xunit;
using TurboHTTP.Server.Lifecycle;

namespace TurboHTTP.Tests.Server.Lifecycle;

public sealed class ConnectionActorSpec : TestKit
{
    private sealed class ParentActor : ReceiveActor
    {
        public sealed record CreateConnection(string ConnectionId);

        private IActorRef? _testActor;

        public ParentActor()
        {
            Receive<CreateConnection>(msg =>
            {
                _testActor = Sender;
                var connectionActor = Context.ActorOf(
                    ConnectionActor.Create(msg.ConnectionId),
                    "connection");
                _testActor.Tell(connectionActor, ActorRefs.NoSender);
            });

            Receive<ConnectionActor.ConnectionCompleted>(msg =>
            {
                _testActor?.Tell(msg, ActorRefs.NoSender);
            });
        }
    }

    [Fact(Timeout = 5000)]
    public void ConnectionActor_should_report_completion_on_stream_success()
    {
        var parentActor = Sys.ActorOf(Props.Create(() => new ParentActor()), "parent");

        parentActor.Tell(new ParentActor.CreateConnection("conn-1"), TestActor);
        var connectionActor = ExpectMsg<IActorRef>(cancellationToken: TestContext.Current.CancellationToken);

        connectionActor.Tell(new ConnectionActor.StreamCompleted(null));

        var completed = ExpectMsg<ConnectionActor.ConnectionCompleted>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("conn-1", completed.ConnectionId);
        Assert.Equal(ConnectionCompletionReason.Normal, completed.Reason);
    }

    [Fact(Timeout = 5000)]
    public void ConnectionActor_should_report_error_on_stream_failure()
    {
        var parentActor = Sys.ActorOf(Props.Create(() => new ParentActor()), "parent2");

        parentActor.Tell(new ParentActor.CreateConnection("conn-2"), TestActor);
        var connectionActor = ExpectMsg<IActorRef>(cancellationToken: TestContext.Current.CancellationToken);

        connectionActor.Tell(new ConnectionActor.StreamCompleted(new InvalidOperationException("boom")));

        var completed = ExpectMsg<ConnectionActor.ConnectionCompleted>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("conn-2", completed.ConnectionId);
        Assert.Equal(ConnectionCompletionReason.Error, completed.Reason);
    }

    [Fact(Timeout = 5000)]
    public void ConnectionActor_should_stop_self_after_stream_completes()
    {
        var parentActor = Sys.ActorOf(Props.Create(() => new ParentActor()), "parent3");

        parentActor.Tell(new ParentActor.CreateConnection("conn-3"), TestActor);
        var connectionActor = ExpectMsg<IActorRef>(cancellationToken: TestContext.Current.CancellationToken);

        connectionActor.Tell(new ConnectionActor.StreamCompleted(null));

        ExpectMsg<ConnectionActor.ConnectionCompleted>(cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 5000)]
    public void ConnectionActor_should_report_timeout_on_graceful_stop_without_stream()
    {
        var parentActor = Sys.ActorOf(Props.Create(() => new ParentActor()), "parent4");

        parentActor.Tell(new ParentActor.CreateConnection("conn-4"), TestActor);
        var connectionActor = ExpectMsg<IActorRef>(cancellationToken: TestContext.Current.CancellationToken);

        connectionActor.Tell(new ConnectionActor.GracefulStop(TimeSpan.FromMilliseconds(200)));

        var completed = ExpectMsg<ConnectionActor.ConnectionCompleted>(TimeSpan.FromSeconds(3), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("conn-4", completed.ConnectionId);
        Assert.Equal(ConnectionCompletionReason.Timeout, completed.Reason);
    }

    [Fact(Timeout = 5000)]
    public void ConnectionActor_should_report_timeout_when_drain_exceeds_limit()
    {
        var parentActor = Sys.ActorOf(Props.Create(() => new ParentActor()), "parent5");

        parentActor.Tell(new ParentActor.CreateConnection("conn-5"), TestActor);
        var connectionActor = ExpectMsg<IActorRef>(cancellationToken: TestContext.Current.CancellationToken);

        connectionActor.Tell(new ConnectionActor.GracefulStop(TimeSpan.FromMilliseconds(200)));

        var completed = ExpectMsg<ConnectionActor.ConnectionCompleted>(TimeSpan.FromSeconds(3),
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("conn-5", completed.ConnectionId);
        Assert.Equal(ConnectionCompletionReason.Timeout, completed.Reason);
    }
}