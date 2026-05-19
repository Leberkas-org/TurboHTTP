using System.Net;
using System.Reflection;
using System.Threading.Channels;
using Akka.Actor;
using TurboHTTP.Client;
using TurboHTTP.Internal;
using TurboHTTP.Streams.Lifecycle;

namespace TurboHTTP.Tests.Client;

public sealed class TurboHttpClientSpec
{
    private static readonly ConstructorInfo TurboHttpClientCtor = typeof(TurboHttpClient)
        .GetConstructor(
            BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            [
                typeof(ChannelWriter<HttpRequestMessage>),
                typeof(ChannelReader<HttpResponseMessage>),
                typeof(TurboRequestOptions),
                typeof(NamedClientConsumerRegistration)
            ],
            null)!;

    private static TurboHttpClient CreateTestClient(
        Channel<HttpRequestMessage>? requests = null,
        Channel<HttpResponseMessage>? responses = null,
        Guid? consumerId = null,
        TimeSpan? timeout = null)
    {
        requests ??= Channel.CreateUnbounded<HttpRequestMessage>();
        responses ??= Channel.CreateUnbounded<HttpResponseMessage>();
        consumerId ??= Guid.NewGuid();
        timeout ??= TimeSpan.FromSeconds(30);

        var registration = new NamedClientConsumerRegistration(
            ActorRefs.Nobody, "test", consumerId.Value);

        var options = new TurboRequestOptions(
            BaseAddress: null,
            DefaultRequestHeaders: new HttpRequestMessage().Headers,
            DefaultRequestVersion: HttpVersion.Version11,
            DefaultVersionPolicy: HttpVersionPolicy.RequestVersionOrLower,
            Timeout: timeout.Value,
            Credentials: null,
            PreAuthenticate: false);

        var client = (TurboHttpClient)TurboHttpClientCtor.Invoke(
            [requests.Writer, responses.Reader, options, registration]);

        return client;
    }

    [Fact(Timeout = 5000)]
    public async Task SendAsync_should_stamp_consumer_id_on_request()
    {
        var consumerId = Guid.NewGuid();
        var requests = Channel.CreateUnbounded<HttpRequestMessage>();
        var responses = Channel.CreateUnbounded<HttpResponseMessage>();

        using var client = CreateTestClient(requests, responses, consumerId);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var sendTask = client.SendAsync(request, TestContext.Current.CancellationToken);

        var observed = await requests.Reader.ReadAsync(TestContext.Current.CancellationToken);
        Assert.True(observed.Options.TryGetValue(OptionsKey.ConsumerIdKey, out var observedId));
        Assert.Equal(consumerId, observedId);

        // Complete the pending request to allow SendAsync to complete
        Assert.True(observed.Options.TryGetValue(OptionsKey.Key, out var pending));
        Assert.True(observed.Options.TryGetValue(OptionsKey.VersionKey, out var ver));
        var response = new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = observed };
        pending.TrySetResult(response, ver);

        var result = await sendTask;
        Assert.NotNull(result);
    }

    [Fact(Timeout = 5000)]
    public async Task SendAsync_should_set_pending_request_tcs_on_request()
    {
        var requests = Channel.CreateUnbounded<HttpRequestMessage>();
        var responses = Channel.CreateUnbounded<HttpResponseMessage>();

        using var client = CreateTestClient(requests, responses);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var sendTask = client.SendAsync(request, TestContext.Current.CancellationToken);

        var observed = await requests.Reader.ReadAsync(TestContext.Current.CancellationToken);
        Assert.True(observed.Options.TryGetValue(OptionsKey.Key, out var pending));
        Assert.NotNull(pending);
        Assert.True(observed.Options.TryGetValue(OptionsKey.VersionKey, out var version));
        Assert.NotEqual((short)0, version);

        // Complete the pending request
        var response = new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = observed };
        pending.TrySetResult(response, version);

        var result = await sendTask;
        Assert.NotNull(result);
    }

    [Fact(Timeout = 5000)]
    public async Task SendAsync_should_return_response_when_tcs_is_completed()
    {
        var requests = Channel.CreateUnbounded<HttpRequestMessage>();
        var responses = Channel.CreateUnbounded<HttpResponseMessage>();

        using var client = CreateTestClient(requests, responses);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var sendTask = client.SendAsync(request, TestContext.Current.CancellationToken);

        var observed = await requests.Reader.ReadAsync(TestContext.Current.CancellationToken);
        Assert.True(observed.Options.TryGetValue(OptionsKey.Key, out var pending));
        Assert.True(observed.Options.TryGetValue(OptionsKey.VersionKey, out var ver));

        var expectedResponse = new HttpResponseMessage(HttpStatusCode.Created)
        {
            RequestMessage = observed,
            Content = new StringContent("test")
        };
        pending.TrySetResult(expectedResponse, ver);

        var result = await sendTask;
        Assert.NotNull(result);
        Assert.Equal(HttpStatusCode.Created, result.StatusCode);
        Assert.Same(expectedResponse, result);
    }

    [Fact(Timeout = 5000)]
    public void SendAsync_should_throw_when_disposed()
    {
        var requests = Channel.CreateUnbounded<HttpRequestMessage>();
        var responses = Channel.CreateUnbounded<HttpResponseMessage>();

        var client = CreateTestClient(requests, responses);
        client.Dispose();

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var ex = Assert.ThrowsAsync<ObjectDisposedException>(() =>
            client.SendAsync(request, TestContext.Current.CancellationToken));

        Assert.NotNull(ex);
    }

    [Fact(Timeout = 5000)]
    public async Task SendAsync_should_throw_on_closed_request_channel()
    {
        var requests = Channel.CreateUnbounded<HttpRequestMessage>();
        var responses = Channel.CreateUnbounded<HttpResponseMessage>();

        using var client = CreateTestClient(requests, responses);

        // Close the request channel
        requests.Writer.TryComplete();

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var ex = await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            client.SendAsync(request, TestContext.Current.CancellationToken));

        Assert.NotNull(ex);
    }

    [Fact(Timeout = 5000)]
    public async Task SendAsync_should_timeout_when_no_response()
    {
        var requests = Channel.CreateUnbounded<HttpRequestMessage>();
        var responses = Channel.CreateUnbounded<HttpResponseMessage>();

        using var client = CreateTestClient(requests, responses, timeout: TimeSpan.FromMilliseconds(100));
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        // Send the request but don't complete the TCS
        var sendTask = client.SendAsync(request, TestContext.Current.CancellationToken);

        // Read and ignore the request (to avoid channel closure)
        await requests.Reader.ReadAsync(TestContext.Current.CancellationToken);

        // Wait for the timeout
        var ex = await Assert.ThrowsAsync<OperationCanceledException>(() => sendTask);

        Assert.NotNull(ex);
    }

    [Fact(Timeout = 5000)]
    public async Task SendAsync_should_honor_cancellation_token()
    {
        var requests = Channel.CreateUnbounded<HttpRequestMessage>();
        var responses = Channel.CreateUnbounded<HttpResponseMessage>();

        using var client = CreateTestClient(requests, responses);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var ex = await Assert.ThrowsAsync<TaskCanceledException>(() => client.SendAsync(request, cts.Token));

        Assert.NotNull(ex);
    }

    [Fact(Timeout = 5000)]
    public void Dispose_should_be_idempotent()
    {
        var requests = Channel.CreateUnbounded<HttpRequestMessage>();
        var responses = Channel.CreateUnbounded<HttpResponseMessage>();

        var client = CreateTestClient(requests, responses);

        // Should not throw
        client.Dispose();
        client.Dispose();
    }

    [Fact(Timeout = 5000)]
    public async Task CancelPendingRequests_should_cancel_inflight_requests()
    {
        var requests = Channel.CreateUnbounded<HttpRequestMessage>();
        var responses = Channel.CreateUnbounded<HttpResponseMessage>();

        using var client = CreateTestClient(requests, responses);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var sendTask = client.SendAsync(request, TestContext.Current.CancellationToken);

        // Read the request to prevent channel closure
        await requests.Reader.ReadAsync(TestContext.Current.CancellationToken);

        // Cancel all pending requests
        client.CancelPendingRequests();

        // The send task should be cancelled
        var ex = await Assert.ThrowsAsync<OperationCanceledException>(() => sendTask);

        Assert.NotNull(ex);
    }

    [Fact(Timeout = 5000)]
    public void Property_setters_should_update_cached_options()
    {
        var requests = Channel.CreateUnbounded<HttpRequestMessage>();
        var responses = Channel.CreateUnbounded<HttpResponseMessage>();

        using var client = CreateTestClient(requests, responses);

        var newBaseAddress = new Uri("http://example.com/");
        var newVersion = HttpVersion.Version20;
        var newTimeout = TimeSpan.FromMilliseconds(5000);

        client.BaseAddress = newBaseAddress;
        client.DefaultRequestVersion = newVersion;
        client.Timeout = newTimeout;

        var cached = client.CachedOptions;
        Assert.Equal(newBaseAddress, cached.BaseAddress);
        Assert.Equal(newVersion, cached.DefaultRequestVersion);
        Assert.Equal(newTimeout, cached.Timeout);
    }
}