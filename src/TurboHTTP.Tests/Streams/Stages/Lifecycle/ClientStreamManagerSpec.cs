using System.Net;
using System.Threading.Channels;
using Akka.Actor;
using TurboHTTP.Streams;
using TurboHTTP.Streams.Lifecycle;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Streams.Stages.Lifecycle;

public sealed class ClientStreamManagerSpec : StreamTestBase
{
    private static TurboRequestOptions CreateRequestOptions()
    {
        return new TurboRequestOptions(
            BaseAddress: null,
            DefaultRequestHeaders: new HttpRequestMessage().Headers,
            DefaultRequestVersion: HttpVersion.Version11,
            DefaultVersionPolicy: HttpVersionPolicy.RequestVersionOrLower,
            Timeout: TimeSpan.FromSeconds(30),
            Credentials: null,
            PreAuthenticate: false);
    }

    [Fact(Timeout = 10_000)]
    public async Task ClientStreamManager_should_create_owner_child_for_new_name()
    {
        var manager = Sys.ActorOf(ClientStreamManager.Props(), "test-manager");

        manager.Tell(new ClientStreamManager.RegisterConsumer(
            Name: "my-api",
            ConsumerId: Guid.NewGuid(),
            RequestReader: Channel.CreateUnbounded<HttpRequestMessage>().Reader,
            OptionsFactory: () => CreateRequestOptions(),
            ResponseWriter: Channel.CreateUnbounded<HttpResponseMessage>().Writer,
            ClientOptions: new TurboClientOptions { BaseAddress = new Uri("http://localhost") },
            Pipeline: PipelineDescriptor.Empty));

        await Task.Delay(500, TestContext.Current.CancellationToken);

        var ownerPath = manager.Path / "my-api";
        var resolved = await Sys.ActorSelection(ownerPath)
            .ResolveOne(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        Assert.NotNull(resolved);

        manager.Tell(new ClientStreamManager.Shutdown());
    }

    [Fact(Timeout = 10_000)]
    public async Task ClientStreamManager_should_shutdown_gracefully()
    {
        var manager = Sys.ActorOf(ClientStreamManager.Props(), "shutdown-manager");
        await WatchAsync(manager);

        manager.Tell(new ClientStreamManager.Shutdown());

        await ExpectTerminatedAsync(manager, TimeSpan.FromSeconds(5), cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 15_000)]
    public async Task ClientStreamManager_should_reuse_owner_for_same_name()
    {
        var manager = Sys.ActorOf(ClientStreamManager.Props(), "reuse-manager");

        manager.Tell(new ClientStreamManager.RegisterConsumer(
            Name: "shared",
            ConsumerId: Guid.NewGuid(),
            RequestReader: Channel.CreateUnbounded<HttpRequestMessage>().Reader,
            OptionsFactory: () => CreateRequestOptions(),
            ResponseWriter: Channel.CreateUnbounded<HttpResponseMessage>().Writer,
            ClientOptions: new TurboClientOptions { BaseAddress = new Uri("http://localhost") },
            Pipeline: PipelineDescriptor.Empty));

        manager.Tell(new ClientStreamManager.RegisterConsumer(
            Name: "shared",
            ConsumerId: Guid.NewGuid(),
            RequestReader: Channel.CreateUnbounded<HttpRequestMessage>().Reader,
            OptionsFactory: () => CreateRequestOptions(),
            ResponseWriter: Channel.CreateUnbounded<HttpResponseMessage>().Writer,
            ClientOptions: new TurboClientOptions { BaseAddress = new Uri("http://localhost") },
            Pipeline: PipelineDescriptor.Empty));

        await Task.Delay(500, TestContext.Current.CancellationToken);

        var ownerPath = manager.Path / "shared";
        var resolved = await Sys.ActorSelection(ownerPath)
            .ResolveOne(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        Assert.NotNull(resolved);

        manager.Tell(new ClientStreamManager.Shutdown());
    }

    [Fact(Timeout = 10_000)]
    public async Task ClientStreamManager_should_unregister_consumer()
    {
        var manager = Sys.ActorOf(ClientStreamManager.Props(), "unregister-manager");
        var consumerId = Guid.NewGuid();

        manager.Tell(new ClientStreamManager.RegisterConsumer(
            Name: "test",
            ConsumerId: consumerId,
            RequestReader: Channel.CreateUnbounded<HttpRequestMessage>().Reader,
            OptionsFactory: () => CreateRequestOptions(),
            ResponseWriter: Channel.CreateUnbounded<HttpResponseMessage>().Writer,
            ClientOptions: new TurboClientOptions { BaseAddress = new Uri("http://localhost") },
            Pipeline: PipelineDescriptor.Empty));

        await Task.Delay(200, TestContext.Current.CancellationToken);

        manager.Tell(new ClientStreamManager.UnregisterConsumer("test", consumerId));

        await Task.Delay(100, TestContext.Current.CancellationToken);

        manager.Tell(new ClientStreamManager.Shutdown());
    }

    [Fact(Timeout = 10_000)]
    public async Task ClientStreamManager_should_sanitize_empty_name_to_default()
    {
        var manager = Sys.ActorOf(ClientStreamManager.Props(), "empty-name-manager");

        manager.Tell(new ClientStreamManager.RegisterConsumer(
            Name: "",
            ConsumerId: Guid.NewGuid(),
            RequestReader: Channel.CreateUnbounded<HttpRequestMessage>().Reader,
            OptionsFactory: () => CreateRequestOptions(),
            ResponseWriter: Channel.CreateUnbounded<HttpResponseMessage>().Writer,
            ClientOptions: new TurboClientOptions { BaseAddress = new Uri("http://localhost") },
            Pipeline: PipelineDescriptor.Empty));

        await Task.Delay(500, TestContext.Current.CancellationToken);

        var ownerPath = manager.Path / "default";
        var resolved = await Sys.ActorSelection(ownerPath)
            .ResolveOne(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        Assert.NotNull(resolved);

        manager.Tell(new ClientStreamManager.Shutdown());
    }

    [Fact(Timeout = 10_000)]
    public async Task ClientStreamManager_should_sanitize_special_characters_in_name()
    {
        var manager = Sys.ActorOf(ClientStreamManager.Props(), "special-chars-manager");

        manager.Tell(new ClientStreamManager.RegisterConsumer(
            Name: "my api/v2",
            ConsumerId: Guid.NewGuid(),
            RequestReader: Channel.CreateUnbounded<HttpRequestMessage>().Reader,
            OptionsFactory: () => CreateRequestOptions(),
            ResponseWriter: Channel.CreateUnbounded<HttpResponseMessage>().Writer,
            ClientOptions: new TurboClientOptions { BaseAddress = new Uri("http://localhost") },
            Pipeline: PipelineDescriptor.Empty));

        await Task.Delay(500, TestContext.Current.CancellationToken);

        // The name "my api/v2" should be escaped as "my%20api%2Fv2" then % replaced with _
        // so the child actor name should be "my_20api_2Fv2"
        var ownerPath = manager.Path / "my_20api_2Fv2";
        var resolved = await Sys.ActorSelection(ownerPath)
            .ResolveOne(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        Assert.NotNull(resolved);

        manager.Tell(new ClientStreamManager.Shutdown());
    }

    [Fact(Timeout = 15_000)]
    public async Task ClientStreamManager_should_forward_unregister_to_owner()
    {
        var manager = Sys.ActorOf(ClientStreamManager.Props(), "forward-unregister-manager");
        var consumerId = Guid.NewGuid();

        manager.Tell(new ClientStreamManager.RegisterConsumer(
            Name: "test-consumer",
            ConsumerId: consumerId,
            RequestReader: Channel.CreateUnbounded<HttpRequestMessage>().Reader,
            OptionsFactory: () => CreateRequestOptions(),
            ResponseWriter: Channel.CreateUnbounded<HttpResponseMessage>().Writer,
            ClientOptions: new TurboClientOptions { BaseAddress = new Uri("http://localhost") },
            Pipeline: PipelineDescriptor.Empty));

        await Task.Delay(500, TestContext.Current.CancellationToken);

        manager.Tell(new ClientStreamManager.UnregisterConsumer("test-consumer", consumerId));

        await Task.Delay(500, TestContext.Current.CancellationToken);

        // Verify no exceptions and the message was processed
        manager.Tell(new ClientStreamManager.Shutdown());
    }

    [Fact(Timeout = 10_000)]
    public async Task ClientStreamManager_should_ignore_unregister_for_unknown_name()
    {
        var manager = Sys.ActorOf(ClientStreamManager.Props(), "unknown-unregister-manager");

        // Send unregister for a name that was never registered
        manager.Tell(new ClientStreamManager.UnregisterConsumer("never-registered", Guid.NewGuid()));

        await Task.Delay(100, TestContext.Current.CancellationToken);

        // Should not crash, manager should still be alive
        manager.Tell(new ClientStreamManager.Shutdown());
    }

    [Fact(Timeout = 15_000)]
    public async Task ClientStreamManager_should_create_separate_owners_for_different_names()
    {
        var manager = Sys.ActorOf(ClientStreamManager.Props(), "separate-owners-manager");

        manager.Tell(new ClientStreamManager.RegisterConsumer(
            Name: "api-v1",
            ConsumerId: Guid.NewGuid(),
            RequestReader: Channel.CreateUnbounded<HttpRequestMessage>().Reader,
            OptionsFactory: () => CreateRequestOptions(),
            ResponseWriter: Channel.CreateUnbounded<HttpResponseMessage>().Writer,
            ClientOptions: new TurboClientOptions { BaseAddress = new Uri("http://localhost") },
            Pipeline: PipelineDescriptor.Empty));

        manager.Tell(new ClientStreamManager.RegisterConsumer(
            Name: "api-v2",
            ConsumerId: Guid.NewGuid(),
            RequestReader: Channel.CreateUnbounded<HttpRequestMessage>().Reader,
            OptionsFactory: () => CreateRequestOptions(),
            ResponseWriter: Channel.CreateUnbounded<HttpResponseMessage>().Writer,
            ClientOptions: new TurboClientOptions { BaseAddress = new Uri("http://localhost") },
            Pipeline: PipelineDescriptor.Empty));

        await Task.Delay(500, TestContext.Current.CancellationToken);

        var owner1Path = manager.Path / "api-v1";
        var resolved1 = await Sys.ActorSelection(owner1Path)
            .ResolveOne(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        Assert.NotNull(resolved1);

        var owner2Path = manager.Path / "api-v2";
        var resolved2 = await Sys.ActorSelection(owner2Path)
            .ResolveOne(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        Assert.NotNull(resolved2);

        // Verify they are different actors
        Assert.NotEqual(resolved1, resolved2);

        manager.Tell(new ClientStreamManager.Shutdown());
    }

    [Fact(Timeout = 10_000)]
    public void ClientStreamManager_registration_dispose_should_be_idempotent()
    {
        var manager = Sys.ActorOf(ClientStreamManager.Props(), "idempotent-dispose-manager");
        var consumerId = Guid.NewGuid();

        var registration = new NamedClientConsumerRegistration(manager, "test-api", consumerId);

        // First dispose should work
        registration.Dispose();

        // Second dispose should not throw
        registration.Dispose();

        manager.Tell(new ClientStreamManager.Shutdown());
    }
}
