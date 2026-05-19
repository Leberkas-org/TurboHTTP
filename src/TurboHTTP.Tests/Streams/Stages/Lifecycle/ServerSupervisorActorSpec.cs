using Akka.Actor;
using Akka.TestKit.Xunit;
using TurboHTTP.Streams.Lifecycle;

namespace TurboHTTP.Tests.Streams.Stages.Lifecycle;

public sealed class ServerSupervisorActorSpec : TestKit
{
    [Fact(Timeout = 5000)]
    public void Supervisor_should_track_connection_started()
    {
        var supervisor = Sys.ActorOf(Props.Create(() => new ServerSupervisorActor()));

        supervisor.Tell(new ListenerActor.ConnectionStarted("conn-1", TestActor));
        supervisor.Tell(new ServerSupervisorActor.GetConnectionCount());

        var count = ExpectMsg<int>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(1, count);
    }

    [Fact(Timeout = 5000)]
    public void Supervisor_should_decrement_on_connection_completed()
    {
        var supervisor = Sys.ActorOf(Props.Create(() => new ServerSupervisorActor()));

        supervisor.Tell(new ListenerActor.ConnectionStarted("conn-1", TestActor));
        supervisor.Tell(new ConnectionActor.ConnectionCompleted("conn-1", ConnectionCompletionReason.Normal));
        supervisor.Tell(new ServerSupervisorActor.GetConnectionCount());

        var count = ExpectMsg<int>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(0, count);
    }
}
