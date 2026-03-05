using System.Net;
using System.Threading.Channels;
using TurboHttp.Client;
using TurboHttp.Streams;

namespace TurboHttp.StreamTests.Client;

public sealed class TurboClientStreamManagerTests : StreamTestBase
{
    // ── helpers ────────────────────────────────────────────────────────────────

    private sealed class FakePool : IHostConnectionPool
    {
        private readonly Action<HttpRequestMessage>? _onSend;

        public FakePool(Action<HttpRequestMessage>? onSend = null)
        {
            _onSend = onSend;
        }

        public void Send(HttpRequestMessage request)
        {
            _onSend?.Invoke(request);
        }
    }

    // ── tests ──────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "MGR-001: Manager creates without throwing; Requests and Responses are non-null")]
    public void MGR_001_ManagerCreatesSuccessfully()
    {
        var options = new TurboClientOptions { BaseAddress = new Uri("http://test.host/") };
        var manager = new TurboClientStreamManager(options, Sys);

        Assert.NotNull(manager.Requests);
        Assert.NotNull(manager.Responses);
    }

    [Fact(Timeout = 10_000, DisplayName = "MGR-002: Writing a request with relative URI → enriched to absolute URI at pool")]
    public async Task MGR_002_RequestEnrichedWithBaseAddress_WhenWrittenToChannel()
    {
        var capturedRequests = new List<HttpRequestMessage>();
        var firstRequestArrived = new TaskCompletionSource();

        var options = new TurboClientOptions { BaseAddress = new Uri("http://test.host/") };
        var manager = new TurboClientStreamManager(options, Sys);

        // Set before first element flows — safe because channel is empty at this point
        manager.HostRoutingStage.PoolFactory = (opts, sys, cb) =>
            new FakePool(req =>
            {
                capturedRequests.Add(req);
                firstRequestArrived.TrySetResult();
            });

        await manager.Requests.WriteAsync(
            new HttpRequestMessage(HttpMethod.Get, new Uri("/ping", UriKind.Relative)));

        await firstRequestArrived.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Single(capturedRequests);
        Assert.Equal("http://test.host/ping", capturedRequests[0].RequestUri?.ToString());
    }

    [Fact(Timeout = 10_000, DisplayName = "MGR-003: Response callback → response appears on Responses channel")]
    public async Task MGR_003_ResponseCallback_WritesToResponsesChannel()
    {
        Action<HttpResponseMessage>? capturedCallback = null;
        var callbackCaptured = new TaskCompletionSource();

        var options = new TurboClientOptions { BaseAddress = new Uri("http://test.host/") };
        var manager = new TurboClientStreamManager(options, Sys);

        manager.HostRoutingStage.PoolFactory = (opts, sys, cb) =>
        {
            capturedCallback = cb;
            callbackCaptured.TrySetResult();
            return new FakePool();
        };

        // Trigger pool creation by sending a request
        await manager.Requests.WriteAsync(
            new HttpRequestMessage(HttpMethod.Get, "http://test.host/ping"));

        await callbackCaptured.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Simulate a response arriving from the connection pool
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        capturedCallback!.Invoke(response);

        // Wait for the response to propagate through the stream to the channel
        var readResponse = await ReadFromChannelAsync(manager.Responses, TimeSpan.FromSeconds(5));

        Assert.NotNull(readResponse);
        Assert.Equal(HttpStatusCode.OK, readResponse.StatusCode);
    }

    [Fact(DisplayName = "MGR-004: Bounded channel returns false on TryWrite when full (backpressure semantics)")]
    public void MGR_004_BoundedChannel_TryWriteReturnsFalseWhenFull()
    {
        // Verify that System.Threading.Channels bounded channels enforce capacity,
        // which is the back-pressure mechanism used by TurboClientStreamManager.
        var bounded = Channel.CreateBounded<HttpRequestMessage>(
            new BoundedChannelOptions(2) { FullMode = BoundedChannelFullMode.Wait });

        var r1 = new HttpRequestMessage(HttpMethod.Get, "http://a.test/1");
        var r2 = new HttpRequestMessage(HttpMethod.Get, "http://a.test/2");
        var r3 = new HttpRequestMessage(HttpMethod.Get, "http://a.test/3");

        Assert.True(bounded.Writer.TryWrite(r1));
        Assert.True(bounded.Writer.TryWrite(r2));
        Assert.False(bounded.Writer.TryWrite(r3)); // channel full — backpressure kicks in
    }

    // ── utilities ──────────────────────────────────────────────────────────────

    private static async Task<HttpResponseMessage?> ReadFromChannelAsync(
        ChannelReader<HttpResponseMessage> reader,
        TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            return await reader.ReadAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }
}
