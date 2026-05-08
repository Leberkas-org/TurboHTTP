using System.Net;
using System.Threading.Channels;
using Akka.Actor;
using TurboHTTP.Streams;
using TurboHTTP.Streams.Lifecycle;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.StreamTests.Streams.Lifecycle;

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
            FallbackResponseWriter: Channel.CreateUnbounded<HttpResponseMessage>().Writer,
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
        Watch(manager);

        manager.Tell(new ClientStreamManager.Shutdown());

        ExpectTerminated(manager, TimeSpan.FromSeconds(5), cancellationToken: TestContext.Current.CancellationToken);
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
            FallbackResponseWriter: Channel.CreateUnbounded<HttpResponseMessage>().Writer,
            ClientOptions: new TurboClientOptions { BaseAddress = new Uri("http://localhost") },
            Pipeline: PipelineDescriptor.Empty));

        manager.Tell(new ClientStreamManager.RegisterConsumer(
            Name: "shared",
            ConsumerId: Guid.NewGuid(),
            RequestReader: Channel.CreateUnbounded<HttpRequestMessage>().Reader,
            OptionsFactory: () => CreateRequestOptions(),
            FallbackResponseWriter: Channel.CreateUnbounded<HttpResponseMessage>().Writer,
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
            FallbackResponseWriter: Channel.CreateUnbounded<HttpResponseMessage>().Writer,
            ClientOptions: new TurboClientOptions { BaseAddress = new Uri("http://localhost") },
            Pipeline: PipelineDescriptor.Empty));

        await Task.Delay(200, TestContext.Current.CancellationToken);

        manager.Tell(new ClientStreamManager.UnregisterConsumer("test", consumerId));

        await Task.Delay(100, TestContext.Current.CancellationToken);

        manager.Tell(new ClientStreamManager.Shutdown());
    }
}
