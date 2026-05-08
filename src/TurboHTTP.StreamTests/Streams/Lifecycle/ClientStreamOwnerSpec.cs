using System.Net;
using System.Threading.Channels;
using Akka.Actor;
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
        var desc = PipelineDescriptor.Empty;

        var requestChannel = Channel.CreateUnbounded<HttpRequestMessage>(
            new UnboundedChannelOptions { SingleReader = true });
        var responseChannel = Channel.CreateUnbounded<HttpResponseMessage>(
            new UnboundedChannelOptions { SingleWriter = true });

        return new ClientStreamOwner.CreateStreamInstance(
            options,
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

        _ = probe.ExpectMsg<ClientStreamOwner.StreamInstanceCreated>(TimeSpan.FromSeconds(5),
            "Should receive StreamInstanceCreated after creating stream instance",
            TestContext.Current.CancellationToken);

        await actor.GracefulStop(TimeSpan.FromSeconds(2));
    }

    [Fact(Timeout = 10_000)]
    public async Task ClientStreamOwner_should_create_consumer_child_on_register()
    {
        var actor = CreateClientStreamOwner();
        var probe = CreateTestProbe();
        var create = CreateStreamInstanceMessage();

        probe.Send(actor, create);
        _ = probe.ExpectMsg<ClientStreamOwner.StreamInstanceCreated>(
            TimeSpan.FromSeconds(5),
            cancellationToken: TestContext.Current.CancellationToken);

        var consumerId = Guid.NewGuid();
        var consumerRequests = Channel.CreateUnbounded<HttpRequestMessage>();
        var consumerResponses = Channel.CreateUnbounded<HttpResponseMessage>();
        Func<TurboRequestOptions> optionsFactory = () => new TurboRequestOptions(
            BaseAddress: new Uri("https://consumer.example"),
            DefaultRequestHeaders: new HttpRequestMessage().Headers,
            DefaultRequestVersion: HttpVersion.Version11,
            DefaultVersionPolicy: HttpVersionPolicy.RequestVersionOrLower,
            Timeout: TimeSpan.FromSeconds(30),
            Credentials: null,
            PreAuthenticate: false);

        actor.Tell(new ClientStreamOwner.RegisterConsumer(
            consumerId, consumerRequests.Reader, optionsFactory, consumerResponses.Writer));

        await Task.Delay(500, TestContext.Current.CancellationToken);

        var childPath = actor.Path / $"consumer-{consumerId:N}";
        var resolved = await Sys.ActorSelection(childPath)
            .ResolveOne(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        Assert.NotNull(resolved);

        await actor.GracefulStop(TimeSpan.FromSeconds(2));
    }

    [Fact(Timeout = 10_000)]
    public async Task ClientStreamOwner_should_stop_consumer_child_on_unregister()
    {
        var actor = CreateClientStreamOwner();
        var probe = CreateTestProbe();
        var create = CreateStreamInstanceMessage();

        probe.Send(actor, create);
        _ = probe.ExpectMsg<ClientStreamOwner.StreamInstanceCreated>(
            TimeSpan.FromSeconds(5),
            cancellationToken: TestContext.Current.CancellationToken);

        var consumerId = Guid.NewGuid();
        var consumerRequests = Channel.CreateUnbounded<HttpRequestMessage>();
        var consumerResponses = Channel.CreateUnbounded<HttpResponseMessage>();
        Func<TurboRequestOptions> optionsFactory = () => new TurboRequestOptions(
            BaseAddress: new Uri("https://consumer.example"),
            DefaultRequestHeaders: new HttpRequestMessage().Headers,
            DefaultRequestVersion: HttpVersion.Version11,
            DefaultVersionPolicy: HttpVersionPolicy.RequestVersionOrLower,
            Timeout: TimeSpan.FromSeconds(30),
            Credentials: null,
            PreAuthenticate: false);

        actor.Tell(new ClientStreamOwner.RegisterConsumer(
            consumerId, consumerRequests.Reader, optionsFactory, consumerResponses.Writer));

        await Task.Delay(500, TestContext.Current.CancellationToken);

        var childPath = actor.Path / $"consumer-{consumerId:N}";
        var resolved = await Sys.ActorSelection(childPath)
            .ResolveOne(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        Assert.NotNull(resolved);

        actor.Tell(new ClientStreamOwner.UnregisterConsumer(consumerId));

        await Task.Delay(500, TestContext.Current.CancellationToken);

        var notFound = await Sys.ActorSelection(childPath)
            .ResolveOne(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken)
            .ContinueWith(t => t.IsFaulted || t.Result.IsNobody(), TaskScheduler.Default);
        Assert.True(notFound);

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
        probe.Send(actor, new ClientStreamOwner.Shutdown());

        var stopped = await actor.GracefulStop(TimeSpan.FromSeconds(2));
        Assert.True(stopped);
    }

    [Fact(Timeout = 15_000)]
    public async Task ClientStreamOwner_should_timeout_during_shutdown_cleanup()
    {
        var actor = CreateClientStreamOwner();

        actor.Tell(new ClientStreamOwner.Shutdown());

        var stopped = await actor.GracefulStop(TimeSpan.FromSeconds(5));
        Assert.True(stopped);
    }

    [Fact(Timeout = 10_000)]
    public async Task ClientStreamOwner_should_handle_unknown_messages_gracefully()
    {
        var actor = CreateClientStreamOwner();
        var probe = CreateTestProbe();

        probe.Send(actor, "unknown message");

        probe.Send(actor, new ClientStreamOwner.Shutdown());
        var stopped = await actor.GracefulStop(TimeSpan.FromSeconds(2));
        Assert.True(stopped);
    }

    [Fact(Timeout = 10_000)]
    public void ClientStreamOwner_should_complete_on_force_stop()
    {
        var actor = CreateClientStreamOwner();

        Watch(actor);
        actor.Tell(PoisonPill.Instance);
        ExpectTerminated(actor, TimeSpan.FromSeconds(2), cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 15_000)]
    public async Task ClientStreamOwner_should_materialize_per_consumer_ingress_flow_on_registration()
    {
        var actor = CreateClientStreamOwner();
        var probe = CreateTestProbe();
        var create = CreateStreamInstanceMessage();

        probe.Send(actor, create);
        _ = probe.ExpectMsg<ClientStreamOwner.StreamInstanceCreated>(
            TimeSpan.FromSeconds(5),
            cancellationToken: TestContext.Current.CancellationToken);

        var consumerId = Guid.NewGuid();
        var consumerRequests = Channel.CreateUnbounded<HttpRequestMessage>();
        var consumerResponses = Channel.CreateUnbounded<HttpResponseMessage>();
        Func<TurboRequestOptions> optionsFactory = () => new TurboRequestOptions(
            BaseAddress: new Uri("https://consumer.example"),
            DefaultRequestHeaders: new HttpRequestMessage().Headers,
            DefaultRequestVersion: HttpVersion.Version11,
            DefaultVersionPolicy: HttpVersionPolicy.RequestVersionOrLower,
            Timeout: TimeSpan.FromSeconds(30),
            Credentials: null,
            PreAuthenticate: false);

        actor.Tell(new ClientStreamOwner.RegisterConsumer(
            consumerId, consumerRequests.Reader, optionsFactory, consumerResponses.Writer));

        await Task.Delay(500, TestContext.Current.CancellationToken);

        var childPath = actor.Path / $"consumer-{consumerId:N}";
        var resolved = await Sys.ActorSelection(childPath)
            .ResolveOne(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        Assert.NotNull(resolved);

        await actor.GracefulStop(TimeSpan.FromSeconds(2));
    }
}
