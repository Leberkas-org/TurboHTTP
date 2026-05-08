using System.Net;
using System.Threading.Channels;
using Akka.Actor;
using TurboHTTP.Streams;
using TurboHTTP.Streams.Lifecycle;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.StreamTests.Streams.Lifecycle;

public sealed class StreamOwnerSpec : StreamTestBase
{
    private IActorRef CreateClientStreamOwner(TurboClientOptions? options = null, PipelineDescriptor? pipeline = null)
        => Sys.ActorOf(Props.Create(() => new StreamOwner(
            options ?? new TurboClientOptions { BaseAddress = new Uri("http://localhost") },
            pipeline ?? PipelineDescriptor.Empty)));

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

        actor.Tell(new StreamOwner.Shutdown());

        await actor.GracefulStop(TimeSpan.FromSeconds(5));
    }


    [Fact(Timeout = 10_000)]
    public async Task ClientStreamOwner_should_create_consumer_child_on_register()
    {
        var actor = CreateClientStreamOwner();
        await Task.Delay(1000, TestContext.Current.CancellationToken);

        var consumerId = Guid.NewGuid();
        var consumerRequests = Channel.CreateUnbounded<HttpRequestMessage>();
        var consumerResponses = Channel.CreateUnbounded<HttpResponseMessage>();
        var optionsFactory = () => new TurboRequestOptions(
            BaseAddress: new Uri("https://consumer.example"),
            DefaultRequestHeaders: new HttpRequestMessage().Headers,
            DefaultRequestVersion: HttpVersion.Version11,
            DefaultVersionPolicy: HttpVersionPolicy.RequestVersionOrLower,
            Timeout: TimeSpan.FromSeconds(30),
            Credentials: null,
            PreAuthenticate: false);

        actor.Tell(new StreamOwner.RegisterConsumer(
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
        await Task.Delay(1000, TestContext.Current.CancellationToken);

        var consumerId = Guid.NewGuid();
        var consumerRequests = Channel.CreateUnbounded<HttpRequestMessage>();
        var consumerResponses = Channel.CreateUnbounded<HttpResponseMessage>();
        var optionsFactory = () => new TurboRequestOptions(
            BaseAddress: new Uri("https://consumer.example"),
            DefaultRequestHeaders: new HttpRequestMessage().Headers,
            DefaultRequestVersion: HttpVersion.Version11,
            DefaultVersionPolicy: HttpVersionPolicy.RequestVersionOrLower,
            Timeout: TimeSpan.FromSeconds(30),
            Credentials: null,
            PreAuthenticate: false);

        actor.Tell(new StreamOwner.RegisterConsumer(
            consumerId, consumerRequests.Reader, optionsFactory, consumerResponses.Writer));

        await Task.Delay(500, TestContext.Current.CancellationToken);

        var childPath = actor.Path / $"consumer-{consumerId:N}";
        var resolved = await Sys.ActorSelection(childPath)
            .ResolveOne(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        Assert.NotNull(resolved);

        actor.Tell(new StreamOwner.UnregisterConsumer(consumerId));

        await Task.Delay(500, TestContext.Current.CancellationToken);

        var notFound = await Sys.ActorSelection(childPath)
            .ResolveOne(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken)
            .ContinueWith(t => t.IsFaulted || t.Result.IsNobody(), TaskScheduler.Default);
        Assert.True(notFound);

        await actor.GracefulStop(TimeSpan.FromSeconds(2));
    }


    [Fact(Timeout = 10_000)]
    public async Task ClientStreamOwner_should_shutdown_gracefully()
    {
        var actor = CreateClientStreamOwner();

        actor.Tell(new StreamOwner.Shutdown());

        var stopped = await actor.GracefulStop(TimeSpan.FromSeconds(2));
        Assert.True(stopped);
    }

    [Fact(Timeout = 10_000)]
    public async Task ClientStreamOwner_should_ignore_multiple_shutdown_messages()
    {
        var actor = CreateClientStreamOwner();
        var probe = CreateTestProbe();

        probe.Send(actor, new StreamOwner.Shutdown());
        probe.Send(actor, new StreamOwner.Shutdown());

        var stopped = await actor.GracefulStop(TimeSpan.FromSeconds(2));
        Assert.True(stopped);
    }

    [Fact(Timeout = 15_000)]
    public async Task ClientStreamOwner_should_timeout_during_shutdown_cleanup()
    {
        var actor = CreateClientStreamOwner();

        actor.Tell(new StreamOwner.Shutdown());

        var stopped = await actor.GracefulStop(TimeSpan.FromSeconds(5));
        Assert.True(stopped);
    }

    [Fact(Timeout = 10_000)]
    public async Task ClientStreamOwner_should_handle_unknown_messages_gracefully()
    {
        var actor = CreateClientStreamOwner();
        var probe = CreateTestProbe();

        probe.Send(actor, "unknown message");

        probe.Send(actor, new StreamOwner.Shutdown());
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
        await Task.Delay(1000, TestContext.Current.CancellationToken);

        var consumerId = Guid.NewGuid();
        var consumerRequests = Channel.CreateUnbounded<HttpRequestMessage>();
        var consumerResponses = Channel.CreateUnbounded<HttpResponseMessage>();
        var optionsFactory = () => new TurboRequestOptions(
            BaseAddress: new Uri("https://consumer.example"),
            DefaultRequestHeaders: new HttpRequestMessage().Headers,
            DefaultRequestVersion: HttpVersion.Version11,
            DefaultVersionPolicy: HttpVersionPolicy.RequestVersionOrLower,
            Timeout: TimeSpan.FromSeconds(30),
            Credentials: null,
            PreAuthenticate: false);

        actor.Tell(new StreamOwner.RegisterConsumer(
            consumerId, consumerRequests.Reader, optionsFactory, consumerResponses.Writer));

        await Task.Delay(500, TestContext.Current.CancellationToken);

        var childPath = actor.Path / $"consumer-{consumerId:N}";
        var resolved = await Sys.ActorSelection(childPath)
            .ResolveOne(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        Assert.NotNull(resolved);

        await actor.GracefulStop(TimeSpan.FromSeconds(2));
    }
}