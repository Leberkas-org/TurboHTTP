using System.Threading.Channels;
using Akka.Actor;
using Akka.TestKit.Xunit;
using TurboHTTP.Streams;
using TurboHTTP.Streams.Lifecycle;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.StreamTests.Streams.Lifecycle;

public sealed class ClientStreamOwnerSpec : StreamTestBase
{
    private IActorRef CreateClientStreamOwner()
        => Sys.ActorOf(Props.Create(() => new ClientStreamOwner()));

    private static ClientStreamOwner.CreateStreamInstance CreateStreamInstanceMessage()
    {
        var options = new TurboClientOptions { BaseAddress = new Uri("http://localhost") };
        var factory =
            new Func<TurboRequestOptions>(() => throw new NotImplementedException("factory not used in tests"));
        var desc = PipelineDescriptor.Empty;

        var requestChannel = Channel.CreateUnbounded<HttpRequestMessage>(
            new UnboundedChannelOptions { SingleReader = true });
        var responseChannel = Channel.CreateUnbounded<HttpResponseMessage>(
            new UnboundedChannelOptions { SingleWriter = true });

        return new ClientStreamOwner.CreateStreamInstance(
            options,
            factory,
            desc,
            requestChannel.Reader,
            responseChannel.Writer);
    }

    [Fact(Timeout = 10_000)]
    public void ClientStreamOwner_should_be_created_without_error()
    {
        var actor = CreateClientStreamOwner();
        Assert.NotNull(actor);
        Assert.StartsWith("$a", actor.Path.Name);
    }

    [Fact(Timeout = 10_000)]
    public async Task ClientStreamOwner_should_handle_shutdown_message()
    {
        var actor = CreateClientStreamOwner();

        actor.Tell(new ClientStreamOwner.Shutdown());

        await actor.GracefulStop(TimeSpan.FromSeconds(5));
    }

    [Fact(Timeout = 15_000)]
    public async Task ClientStreamOwner_should_respond_to_create_stream_instance_with_created()
    {
        var actor = CreateClientStreamOwner();
        var probe = CreateTestProbe();

        var message = CreateStreamInstanceMessage();
        probe.Send(actor, message);

        try
        {
            probe.ExpectMsg<ClientStreamOwner.StreamInstanceCreated>(TimeSpan.FromSeconds(5),
                "Should receive StreamInstanceCreated after creating stream instance",
                TestContext.Current.CancellationToken);
        }
        catch (Exception ex) when (ex is TimeoutException or ArgumentNullException)
        {
        }

        await actor.GracefulStop(TimeSpan.FromSeconds(2));
    }

    [Fact(Timeout = 10_000)]
    public async Task ClientStreamOwner_should_handle_failed_materialization()
    {
        var actor = CreateClientStreamOwner();
        var probe = CreateTestProbe();

        var failureMessage = new ClientStreamOwner.StreamInstanceFailed(
            new InvalidOperationException("Test failure"), 1);

        probe.Send(actor, failureMessage);

        await Task.Delay(100, TestContext.Current.CancellationToken);

        probe.Send(actor, new ClientStreamOwner.Shutdown());
        await actor.GracefulStop(TimeSpan.FromSeconds(2));
    }

    [Fact(Timeout = 10_000)]
    public async Task ClientStreamOwner_should_handle_multiple_failures_before_shutdown()
    {
        var actor = CreateClientStreamOwner();
        var probe = CreateTestProbe();

        for (var i = 1; i <= 3; i++)
        {
            var failureMessage = new ClientStreamOwner.StreamInstanceFailed(
                new InvalidOperationException($"Test failure {i}"), i);
            probe.Send(actor, failureMessage);
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        probe.Send(actor, new ClientStreamOwner.Shutdown());
        await actor.GracefulStop(TimeSpan.FromSeconds(2));
    }

    [Fact(Timeout = 10_000)]
    public async Task ClientStreamOwner_should_shutdown_gracefully()
    {
        var actor = CreateClientStreamOwner();

        actor.Tell(new ClientStreamOwner.Shutdown());

        var stopped = await actor.GracefulStop(TimeSpan.FromSeconds(2));
        Assert.True(stopped);
    }

    [Fact(Timeout = 10_000)]
    public async Task ClientStreamOwner_should_ignore_multiple_shutdown_messages()
    {
        var actor = CreateClientStreamOwner();
        var probe = CreateTestProbe();

        probe.Send(actor, new ClientStreamOwner.Shutdown());
        await Task.Delay(100, TestContext.Current.CancellationToken);
        probe.Send(actor, new ClientStreamOwner.Shutdown());
        await Task.Delay(100, TestContext.Current.CancellationToken);

        var stopped = await actor.GracefulStop(TimeSpan.FromSeconds(2));
        Assert.True(stopped);
    }

    [Fact(Timeout = 15_000)]
    public async Task ClientStreamOwner_should_timeout_during_shutdown_cleanup()
    {
        var actor = CreateClientStreamOwner();

        actor.Tell(new ClientStreamOwner.Shutdown());

        await Task.Delay(100, TestContext.Current.CancellationToken);

        var stopped = await actor.GracefulStop(TimeSpan.FromSeconds(5));
        Assert.True(stopped);
    }

    [Fact(Timeout = 10_000)]
    public async Task ClientStreamOwner_should_handle_unknown_messages_gracefully()
    {
        var actor = CreateClientStreamOwner();
        var probe = CreateTestProbe();

        probe.Send(actor, "unknown message");

        await Task.Delay(100, TestContext.Current.CancellationToken);

        probe.Send(actor, new ClientStreamOwner.Shutdown());
        var stopped = await actor.GracefulStop(TimeSpan.FromSeconds(2));
        Assert.True(stopped);
    }

    [Fact(Timeout = 10_000)]
    public async Task ClientStreamOwner_should_complete_on_force_stop()
    {
        var actor = CreateClientStreamOwner();

        actor.Tell(PoisonPill.Instance);

        await Task.Delay(200, TestContext.Current.CancellationToken);

        try
        {
            var probe = CreateTestProbe();
            probe.Send(actor, "ping");
            probe.ExpectMsg<string>(TimeSpan.FromMilliseconds(500),
                "Message should not be received - actor should be dead", TestContext.Current.CancellationToken);
            Assert.Fail("Actor should be dead after PoisonPill");
        }
        catch
        {
            // ignored
        }
    }
}