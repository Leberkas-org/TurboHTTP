using TurboHTTP.Streams;
using TurboHTTP.Streams.Lifecycle;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.StreamTests.Streams.Lifecycle;

public sealed class ClientStreamManagerSpec : StreamTestBase
{
    private ClientStreamManager CreateStreamManager(
        TurboClientOptions? clientOptions = null,
        Func<TurboRequestOptions>? requestOptionsFactory = null,
        PipelineDescriptor? pipeline = null)
    {
        var options = clientOptions ?? new TurboClientOptions { BaseAddress = new Uri("http://localhost") };
        var factory = requestOptionsFactory ??
                      (() => throw new NotImplementedException("factory not used in tests"));
        var desc = pipeline ?? PipelineDescriptor.Empty;

        return new ClientStreamManager(options, factory, Sys, desc);
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

        var reader = manager.Responses;
        Assert.NotNull(reader);
    }

    [Fact(Timeout = 10_000)]
    public void ClientStreamManager_should_dispose_without_error()
    {
        var manager = CreateStreamManager();
        manager.Dispose();

        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/test");
        var written = manager.Requests.TryWrite(request);
        Assert.False(written, "Should not be able to write after dispose");
    }

    [Fact(Timeout = 10_000)]
    public void ClientStreamManager_should_handle_multiple_dispose_calls()
    {
        var manager = CreateStreamManager();

        manager.Dispose();
        manager.Dispose();
        manager.Dispose();

        Assert.True(true);
    }

    [Fact(Timeout = 10_000)]
    public void ClientStreamManager_should_complete_channels_on_dispose()
    {
        var manager = CreateStreamManager();

        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/test");
        manager.Requests.TryWrite(request);

        manager.Dispose();

        var completedRequests = manager.Requests.TryComplete();
        Assert.False(completedRequests, "Request channel should already be completed");
    }

    [Fact(Timeout = 10_000)]
    public void ClientStreamManager_should_allow_reading_response_channel_after_disposal()
    {
        var manager = CreateStreamManager();

        manager.Dispose();

        var reader = manager.Responses;
        Assert.NotNull(reader);
    }

    [Fact(Timeout = 15_000)]
    public async Task ClientStreamManager_should_allow_graceful_termination_with_when_terminated()
    {
        var manager = CreateStreamManager();

        var terminateTask = manager.WhenTerminatedAsync(TimeSpan.FromSeconds(5));

        manager.Dispose();

        await terminateTask.WaitAsync(TimeSpan.FromSeconds(6), TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 10_000)]
    public void ClientStreamManager_should_support_idempotent_channel_completion()
    {
        var manager = CreateStreamManager();

        manager.Dispose();

        var requestsCompleted = manager.Requests.TryComplete();
        Assert.False(requestsCompleted);
    }

    [Fact(Timeout = 15_000)]
    public void ClientStreamManager_should_allow_configuration_before_disposal()
    {
        var clientOpts = new TurboClientOptions
        {
            BaseAddress = new Uri("https://api.example.com")
        };

        var manager = CreateStreamManager(clientOptions: clientOpts);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/test")
        {
            Version = new Version(1, 1)
        };
        Assert.True(manager.Requests.TryWrite(request));

        manager.Dispose();
    }

    [Fact(Timeout = 10_000)]
    public void ClientStreamManager_should_create_owner_actor_synchronously()
    {
        var manager = CreateStreamManager();
        Assert.NotNull(manager);

        Assert.NotNull(manager.Requests);
        Assert.NotNull(manager.Responses);

        manager.Dispose();
    }

    [Fact(Timeout = 15_000)]
    public async Task ClientStreamManager_should_handle_rapid_dispose_and_termination()
    {
        var manager = CreateStreamManager();

        manager.Dispose();

        var terminateTask = manager.WhenTerminatedAsync(TimeSpan.FromSeconds(2));

        try
        {
            await terminateTask.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        }
        catch (TimeoutException)
        {
            Assert.Fail("Should have terminated within timeout");
        }
    }

    [Fact(Timeout = 10_000)]
    public void ClientStreamManager_should_maintain_channel_semantics_on_disposal()
    {
        var manager = CreateStreamManager();

        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/test");
        var written = manager.Requests.TryWrite(request);
        Assert.True(written);

        manager.Dispose();

        Assert.False(manager.Requests.TryComplete());
    }
}