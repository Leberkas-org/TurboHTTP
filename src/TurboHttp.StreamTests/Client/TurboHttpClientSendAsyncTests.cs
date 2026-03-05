using System.Net;
using TurboHttp.Client;
using TurboHttp.Streams;

namespace TurboHttp.StreamTests.Client;

public sealed class TurboHttpClientSendAsyncTests : StreamTestBase
{
    // ── helpers ────────────────────────────────────────────────────────────────

    private sealed class FakePool : IHostConnectionPool
    {
        private readonly Action<HttpRequestMessage> _onSend;

        public FakePool(Action<HttpRequestMessage> onSend)
        {
            _onSend = onSend;
        }

        public void Send(HttpRequestMessage request) => _onSend(request);
    }

    /// <summary>Immediately responds 200 OK with RequestMessage set to the incoming request.</summary>
    private static IHostConnectionPool RespondingPool(
        Action<HttpResponseMessage> cb,
        HttpStatusCode status = HttpStatusCode.OK)
        => new FakePool(req =>
        {
            var response = new HttpResponseMessage(status) { RequestMessage = req };
            cb(response);
        });

    /// <summary>Captures requests for inspection; still responds 200 OK.</summary>
    private static IHostConnectionPool CapturingPool(
        Action<HttpResponseMessage> cb,
        List<HttpRequestMessage> captured)
        => new FakePool(req =>
        {
            captured.Add(req);
            var response = new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = req };
            cb(response);
        });

    /// <summary>Never sends a response — used for timeout/cancel tests.</summary>
    private static IHostConnectionPool SilentPool()
        => new FakePool(_ => { });

    private TurboHttpClient BuildClient(TurboClientOptions? options = null)
    {
        var opts = options ?? new TurboClientOptions { BaseAddress = new Uri("http://test.host/") };
        return new TurboHttpClient(opts, Sys);
    }

    // ── tests ──────────────────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "CLI-001: Single request → single response returned")]
    public async Task CLI_001_SingleRequest_ReturnsResponse()
    {
        var client = BuildClient();
        client.Manager.HostRoutingStage.PoolFactory = (opts, sys, cb) => RespondingPool(cb);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://test.host/ping");
        var response = await client.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 10_000, DisplayName = "CLI-002: BaseAddress applied before request enters pipeline — URI is absolute at pool")]
    public async Task CLI_002_BaseAddress_Applied_UriIsAbsoluteAtPool()
    {
        var options = new TurboClientOptions { BaseAddress = new Uri("http://test.host/") };
        var client  = BuildClient(options);
        var captured = new List<HttpRequestMessage>();
        client.Manager.HostRoutingStage.PoolFactory = (opts, sys, cb) => CapturingPool(cb, captured);

        var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/resource", UriKind.Relative));
        await client.SendAsync(request, CancellationToken.None);

        Assert.Single(captured);
        Assert.True(captured[0].RequestUri!.IsAbsoluteUri);
        Assert.Equal("http://test.host/resource", captured[0].RequestUri!.ToString());
    }

    [Fact(Timeout = 10_000, DisplayName = "CLI-003: DefaultRequestVersion applied → captured request has correct version")]
    public async Task CLI_003_DefaultRequestVersion_Applied()
    {
        var options = new TurboClientOptions
        {
            BaseAddress           = new Uri("http://test.host/"),
            DefaultRequestVersion = HttpVersion.Version10
        };
        var client   = BuildClient(options);
        var captured = new List<HttpRequestMessage>();
        client.Manager.HostRoutingStage.PoolFactory = (opts, sys, cb) => CapturingPool(cb, captured);

        // Request starts at default 1.1 → enricher should rewrite to 1.0
        var request = new HttpRequestMessage(HttpMethod.Get, "http://test.host/ping");
        await client.SendAsync(request, CancellationToken.None);

        Assert.Single(captured);
        Assert.Equal(HttpVersion.Version10, captured[0].Version);
    }

    [Fact(Timeout = 10_000, DisplayName = "CLI-004: DefaultRequestHeaders merged → header present in captured request")]
    public async Task CLI_004_DefaultRequestHeaders_Merged()
    {
        var client = BuildClient();
        client.DefaultRequestHeaders.Add("X-Default", "value-1");
        var captured = new List<HttpRequestMessage>();
        client.Manager.HostRoutingStage.PoolFactory = (opts, sys, cb) => CapturingPool(cb, captured);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://test.host/ping");
        await client.SendAsync(request, CancellationToken.None);

        Assert.Single(captured);
        Assert.True(captured[0].Headers.Contains("X-Default"));
        Assert.Contains("value-1", captured[0].Headers.GetValues("X-Default"));
    }

    [Fact(Timeout = 10_000, DisplayName = "CLI-005: Explicit headers on request not overridden by DefaultRequestHeaders")]
    public async Task CLI_005_ExplicitHeaders_NotOverridden()
    {
        var client = BuildClient();
        client.DefaultRequestHeaders.Add("X-Override", "default-value");
        var captured = new List<HttpRequestMessage>();
        client.Manager.HostRoutingStage.PoolFactory = (opts, sys, cb) => CapturingPool(cb, captured);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://test.host/ping");
        request.Headers.Add("X-Override", "explicit-value");

        await client.SendAsync(request, CancellationToken.None);

        Assert.Single(captured);
        var values = new List<string>(captured[0].Headers.GetValues("X-Override"));
        Assert.Contains("explicit-value", values);
        Assert.DoesNotContain("default-value", values);
    }

    [Fact(Timeout = 10_000, DisplayName = "CLI-006: Timeout expires before response → TimeoutException thrown")]
    public async Task CLI_006_Timeout_ThrowsTimeoutException()
    {
        var client = BuildClient();
        client.Timeout = TimeSpan.FromMilliseconds(80);
        client.Manager.HostRoutingStage.PoolFactory = (opts, sys, cb) => SilentPool();

        var request = new HttpRequestMessage(HttpMethod.Get, "http://test.host/slow");

        await Assert.ThrowsAsync<TimeoutException>(
            () => client.SendAsync(request, CancellationToken.None));
    }

    [Fact(Timeout = 10_000, DisplayName = "CLI-007: CancellationToken cancelled → TaskCanceledException thrown")]
    public async Task CLI_007_CancellationToken_Cancelled_ThrowsTaskCanceledException()
    {
        var client = BuildClient();
        client.Manager.HostRoutingStage.PoolFactory = (opts, sys, cb) => SilentPool();

        using var cts = new CancellationTokenSource();
        var request = new HttpRequestMessage(HttpMethod.Get, "http://test.host/cancel");
        var sendTask = client.SendAsync(request, cts.Token);

        cts.CancelAfter(TimeSpan.FromMilliseconds(80));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sendTask);
    }

    [Fact(Timeout = 10_000, DisplayName = "CLI-008: 5 sequential requests all complete in order")]
    public async Task CLI_008_FiveSequentialRequests_AllComplete()
    {
        var client = BuildClient();
        client.Manager.HostRoutingStage.PoolFactory = (opts, sys, cb) => RespondingPool(cb);

        var responses = new List<HttpResponseMessage>();
        for (var i = 0; i < 5; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"http://test.host/item/{i}");
            var response = await client.SendAsync(request, CancellationToken.None);
            responses.Add(response);
        }

        Assert.Equal(5, responses.Count);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }

    [Fact(Timeout = 10_000, DisplayName = "CLI-009: 10 concurrent requests all complete")]
    public async Task CLI_009_TenConcurrentRequests_AllComplete()
    {
        var client = BuildClient();
        client.Manager.HostRoutingStage.PoolFactory = (opts, sys, cb) => RespondingPool(cb);

        var tasks = new Task<HttpResponseMessage>[10];
        for (var i = 0; i < 10; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"http://test.host/concurrent/{i}");
            tasks[i] = client.SendAsync(request, CancellationToken.None);
        }

        var responses = await Task.WhenAll(tasks);

        Assert.Equal(10, responses.Length);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }

    [Fact(Timeout = 10_000, DisplayName = "CLI-010: CancelPendingRequests → all in-flight SendAsync tasks throw OperationCanceledException")]
    public async Task CLI_010_CancelPendingRequests_InFlightTasksCancelled()
    {
        var client = BuildClient();
        client.Manager.HostRoutingStage.PoolFactory = (opts, sys, cb) => SilentPool();

        var request = new HttpRequestMessage(HttpMethod.Get, "http://test.host/inflight");
        var sendTask = client.SendAsync(request, CancellationToken.None);

        // Give the request time to enter the pipeline
        await Task.Delay(50);

        client.CancelPendingRequests();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sendTask);
    }

    [Fact(Timeout = 10_000, DisplayName = "CLI-011: After CancelPendingRequests(), new SendAsync works normally")]
    public async Task CLI_011_AfterCancelPendingRequests_NewSendAsyncWorks()
    {
        var client = BuildClient();

        // Phase 1: pool never responds
        Action<HttpResponseMessage>? capturedCb = null;
        var poolCreated = new TaskCompletionSource();
        client.Manager.HostRoutingStage.PoolFactory = (opts, sys, cb) =>
        {
            capturedCb = cb;
            poolCreated.TrySetResult();
            return SilentPool();
        };

        var hangingRequest = new HttpRequestMessage(HttpMethod.Get, "http://test.host/hang");
        var hangingTask = client.SendAsync(hangingRequest, CancellationToken.None);

        await poolCreated.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(30);

        client.CancelPendingRequests();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => hangingTask);

        // Phase 2: new request uses the same (already created) pool — wire up responding behaviour
        // by replacing the factory with a responding one for subsequent host lookups is not possible
        // (pool is cached). Instead, directly invoke the captured callback to simulate a response.
        var newRequest = new HttpRequestMessage(HttpMethod.Get, "http://test.host/after-cancel");
        var sendTask   = client.SendAsync(newRequest, CancellationToken.None);

        // Wait a tick for WriteAsync to deliver the request into the stream, then respond
        await Task.Delay(50);
        var fakeResponse = new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = newRequest };
        capturedCb!.Invoke(fakeResponse);

        var response = await sendTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
