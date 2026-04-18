using System.Net.Http;
using System.Threading.Channels;
using Akka.Actor;
using TurboHTTP.Streams;
using TurboHTTP.Streams.Lifecycle;

namespace TurboHTTP.StreamTests.Streams.Lifecycle;

/// <summary>
/// Tests <see cref="ClientStreamManager"/>: channel lifecycle, owner actor coordination,
/// resource cleanup, and graceful shutdown.
/// </summary>
/// <remarks>
/// ClientStreamManager wraps the ClientStreamOwner actor and manages the request/response
/// channels that serve as the boundary between the client API and the Akka.Streams pipeline.
/// </remarks>
public sealed class ClientStreamManagerSpec : IAsyncLifetime
{
    private ActorSystem? _system;

    public ValueTask InitializeAsync()
    {
        _system = ActorSystem.Create("client-stream-manager-tests");
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_system is not null)
        {
            await _system.Terminate();
        }
    }

    private ClientStreamManager CreateStreamManager(
        TurboClientOptions? clientOptions = null,
        Func<TurboRequestOptions>? requestOptionsFactory = null,
        PipelineDescriptor? pipeline = null)
    {
        var options = clientOptions ?? new TurboClientOptions { BaseAddress = new Uri("http://localhost") };
        var factory = requestOptionsFactory ?? new Func<TurboRequestOptions>(() => throw new NotImplementedException("factory not used in tests"));
        var desc = pipeline ?? PipelineDescriptor.Empty;

        return new ClientStreamManager(options, factory, _system!, desc);
    }

    [Fact(Timeout = 10_000)]
    public void ClientStreamManager_should_create_with_valid_options()
    {
        var manager = CreateStreamManager();
        Assert.NotNull(manager.Requests);
        Assert.NotNull(manager.Responses);
    }

    [Fact(Timeout = 10_000)]
    public void ClientStreamManager_should_provide_request_channel_writer()
    {
        var manager = CreateStreamManager();
        Assert.NotNull(manager.Requests);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/test")
        {
            Version = new Version(1, 1)
        };

        var written = manager.Requests.TryWrite(request);
        Assert.True(written, "Should be able to write request to channel");
    }

    [Fact(Timeout = 10_000)]
    public void ClientStreamManager_should_provide_response_channel_reader()
    {
        var manager = CreateStreamManager();
        Assert.NotNull(manager.Responses);

        // Channel should be readable (even if no responses are written)
        var reader = manager.Responses;
        Assert.NotNull(reader);
    }

    [Fact(Timeout = 10_000)]
    public async Task ClientStreamManager_should_dispose_without_error()
    {
        var manager = CreateStreamManager();
        manager.Dispose();

        // After dispose, channels should be completed
        await Task.Delay(100);

        // Attempting to write should return false (channel completed)
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/test");
        var written = manager.Requests.TryWrite(request);
        Assert.False(written, "Should not be able to write after dispose");
    }

    [Fact(Timeout = 10_000)]
    public async Task ClientStreamManager_should_handle_multiple_dispose_calls()
    {
        var manager = CreateStreamManager();

        manager.Dispose();
        manager.Dispose();  // Should be idempotent
        manager.Dispose();

        // No exception should be thrown
        await Task.Delay(100);
        Assert.True(true);
    }

    [Fact(Timeout = 10_000)]
    public async Task ClientStreamManager_should_complete_channels_on_dispose()
    {
        var manager = CreateStreamManager();

        // Write some data before dispose
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/test");
        manager.Requests.TryWrite(request);

        manager.Dispose();

        await Task.Delay(100);

        // Channels should be completed
        var completedRequests = manager.Requests.TryComplete();
        Assert.False(completedRequests, "Request channel should already be completed");
    }

    [Fact(Timeout = 10_000)]
    public async Task ClientStreamManager_should_allow_reading_response_channel_after_disposal()
    {
        var manager = CreateStreamManager();

        manager.Dispose();

        await Task.Delay(100);

        // Reader should still be accessible (even if channel is completed)
        var reader = manager.Responses;
        Assert.NotNull(reader);
    }

    [Fact(Timeout = 15_000)]
    public async Task ClientStreamManager_should_allow_graceful_termination_with_when_terminated()
    {
        var manager = CreateStreamManager();

        var terminateTask = manager.WhenTerminatedAsync(TimeSpan.FromSeconds(5));

        // Dispose to trigger shutdown
        manager.Dispose();

        // Should complete within timeout
        await terminateTask.WaitAsync(TimeSpan.FromSeconds(6));
    }

    [Fact(Timeout = 10_000)]
    public void ClientStreamManager_should_support_idempotent_channel_completion()
    {
        var manager = CreateStreamManager();

        // Dispose completes the channels
        manager.Dispose();

        // Trying to complete them again should be safe
        var requestsCompleted = manager.Requests.TryComplete();
        Assert.False(requestsCompleted);
    }

    [Fact(Timeout = 15_000)]
    public async Task ClientStreamManager_should_allow_configuration_before_disposal()
    {
        var clientOpts = new TurboClientOptions
        {
            BaseAddress = new Uri("https://api.example.com")
        };

        var manager = CreateStreamManager(clientOptions: clientOpts);

        // Should be fully functional before disposal
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/test")
        {
            Version = new Version(1, 1)
        };
        Assert.True(manager.Requests.TryWrite(request));

        manager.Dispose();
        await Task.Delay(100);
    }

    [Fact(Timeout = 10_000)]
    public void ClientStreamManager_should_create_owner_actor_synchronously()
    {
        // Verify that creating the manager doesn't block or throw
        var manager = CreateStreamManager();
        Assert.NotNull(manager);

        // Owner actor should be created and running
        Assert.NotNull(manager.Requests);
        Assert.NotNull(manager.Responses);

        manager.Dispose();
    }

    [Fact(Timeout = 15_000)]
    public async Task ClientStreamManager_should_handle_rapid_dispose_and_termination()
    {
        var manager = CreateStreamManager();

        manager.Dispose();

        // Rapid termination request
        var terminateTask = manager.WhenTerminatedAsync(TimeSpan.FromSeconds(2));

        try
        {
            await terminateTask.WaitAsync(TimeSpan.FromSeconds(3));
        }
        catch (TimeoutException)
        {
            Assert.True(false, "Should have terminated within timeout");
        }
    }

    [Fact(Timeout = 10_000)]
    public async Task ClientStreamManager_should_maintain_channel_semantics_on_disposal()
    {
        var manager = CreateStreamManager();

        // Write a request before disposal
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/test");
        var written = manager.Requests.TryWrite(request);
        Assert.True(written);

        manager.Dispose();

        await Task.Delay(100);

        // Request channel should be completed
        Assert.False(manager.Requests.TryComplete());
    }
}
