using System.Net;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.IntegrationTests.Shared;
using TurboHttp.IO;
using TurboHttp.Protocol;
using TurboHttp.Streams;
using TurboHttp.Streams.Stages;

namespace TurboHttp.IntegrationTests.Http10;

/// <summary>
/// Integration tests for Http10Engine retry handling.
/// Verifies RFC 9110 §9.2 retry semantics (idempotent retry on 503/408,
/// non-retry on POST, Retry-After, max count, succeed-after-N)
/// against real Kestrel retry routes.
/// </summary>
public sealed class Http10RetryTests : TestKit, IClassFixture<KestrelFixture>
{
    private readonly KestrelFixture _fixture;
    private readonly IMaterializer _materializer;
    private readonly IActorRef _clientManager;

    public Http10RetryTests(KestrelFixture fixture)
    {
        _fixture = fixture;
        _materializer = Sys.Materializer();
        _clientManager = Sys.ActorOf(Props.Create<ClientManager>());
    }

    /// <summary>
    /// Sends a single HTTP/1.0 request through the Http10Engine pipeline.
    /// Each call materialises a fresh pipeline (HTTP/1.0 closes connection after response).
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
    {
        var engine = new Http10Engine();
        var tcpOptions = new TcpOptions
        {
            Host = "127.0.0.1",
            Port = _fixture.Port
        };

        var transport =
            Flow.Create<ITransportItem>()
                .Prepend(Source.Single<ITransportItem>(
                    new ConnectItem(tcpOptions)))
                .Via(new ConnectionStage(_clientManager));

        var flow = engine.CreateFlow().Join(transport);

        var (queue, responseTask) = Source.Queue<HttpRequestMessage>(1, OverflowStrategy.Backpressure)
            .Via(flow)
            .ToMaterialized(Sink.First<HttpResponseMessage>(), Keep.Both)
            .Run(_materializer);

        await queue.OfferAsync(request);
        return await responseTask.WaitAsync(TimeSpan.FromSeconds(10));
    }

    /// <summary>
    /// Sends a request and manually retries using <see cref="RetryEvaluator"/>.
    /// Each retry materialises a new Http10Engine pipeline since HTTP/1.0 has no keep-alive by default.
    /// </summary>
    private async Task<(HttpResponseMessage Response, int AttemptCount)> SendWithRetryAsync(
        HttpRequestMessage request,
        RetryPolicy? policy = null)
    {
        policy ??= RetryPolicy.Default;
        var attemptCount = 0;

        for (var i = 0; i <= policy.MaxRetries; i++)
        {
            attemptCount++;

            // Clone the request for each attempt (original may be disposed after read)
            var attemptRequest = new HttpRequestMessage(request.Method, request.RequestUri)
            {
                Version = HttpVersion.Version10
            };
            foreach (var header in request.Headers)
            {
                attemptRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            var response = await SendAsync(attemptRequest);

            var decision = RetryEvaluator.Evaluate(
                attemptRequest,
                response,
                attemptCount: attemptCount,
                policy: policy);

            if (!decision.ShouldRetry)
            {
                return (response, attemptCount);
            }

            // Honour Retry-After delay if present (capped for test speed)
            if (decision.RetryAfterDelay is { } delay && delay > TimeSpan.Zero)
            {
                var capped = delay > TimeSpan.FromSeconds(2) ? TimeSpan.FromSeconds(2) : delay;
                await Task.Delay(capped);
            }
        }

        // Should not reach here — last attempt returned with no retry
        throw new InvalidOperationException("Exceeded retry loop in test helper");
    }

    [Fact(DisplayName = "RETRY-INT-001: GET /retry/503 retries on 503 Service Unavailable")]
    public async Task Get_503_RetriesIdempotentRequest()
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"http://127.0.0.1:{_fixture.Port}/retry/503")
        {
            Version = HttpVersion.Version10
        };

        var (response, attemptCount) = await SendWithRetryAsync(request);

        // Server always returns 503, so all retries exhaust and we get 503 back
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        // Default policy allows 3 retries, so total attempts = 3
        Assert.Equal(3, attemptCount);
    }

    [Fact(DisplayName = "RETRY-INT-002: GET /retry/408 retries on 408 Request Timeout")]
    public async Task Get_408_RetriesIdempotentRequest()
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"http://127.0.0.1:{_fixture.Port}/retry/408")
        {
            Version = HttpVersion.Version10
        };

        var (response, attemptCount) = await SendWithRetryAsync(request);

        // Server always returns 408, so all retries exhaust and we get 408 back
        Assert.Equal(HttpStatusCode.RequestTimeout, response.StatusCode);
        // Default policy allows 3 retries, so total attempts = 3
        Assert.Equal(3, attemptCount);
    }

    [Fact(DisplayName = "RETRY-INT-003: POST /retry/non-idempotent-503 is NOT retried (RFC 9110 §9.2.2)")]
    public async Task Post_503_NotRetried()
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"http://127.0.0.1:{_fixture.Port}/retry/non-idempotent-503")
        {
            Version = HttpVersion.Version10,
            Content = new StringContent("payload")
        };

        var (response, attemptCount) = await SendWithRetryAsync(request);

        // POST is not idempotent — must not be retried
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(1, attemptCount);
    }

    [Fact(DisplayName = "RETRY-INT-004: GET /retry/503-retry-after/2 returns Retry-After delay")]
    public async Task Get_503_RetryAfter_ParsesDelay()
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"http://127.0.0.1:{_fixture.Port}/retry/503-retry-after/2")
        {
            Version = HttpVersion.Version10
        };

        // Use a policy with max 2 retries so test completes faster
        var policy = new RetryPolicy { MaxRetries = 2, RespectRetryAfter = true };

        // Send first request and evaluate retry decision manually to check Retry-After
        var response = await SendAsync(request);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        var decision = RetryEvaluator.Evaluate(request, response, attemptCount: 1, policy: policy);

        Assert.True(decision.ShouldRetry);
        Assert.NotNull(decision.RetryAfterDelay);
        Assert.Equal(TimeSpan.FromSeconds(2), decision.RetryAfterDelay.Value);
    }

    [Fact(DisplayName = "RETRY-INT-005: Max retry count enforced — stops after MaxRetries")]
    public async Task MaxRetryCount_Enforced()
    {
        var policy = new RetryPolicy { MaxRetries = 2 };

        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"http://127.0.0.1:{_fixture.Port}/retry/503")
        {
            Version = HttpVersion.Version10
        };

        var (response, attemptCount) = await SendWithRetryAsync(request, policy);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        // MaxRetries = 2 means at most 2 attempts total
        Assert.Equal(2, attemptCount);
    }

    [Fact(DisplayName = "RETRY-INT-006: GET /retry/succeed-after/3 succeeds on third attempt")]
    public async Task SucceedAfterN_RetriesUntilSuccess()
    {
        var uniqueKey = Guid.NewGuid().ToString("N");

        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"http://127.0.0.1:{_fixture.Port}/retry/succeed-after/3?key={uniqueKey}")
        {
            Version = HttpVersion.Version10
        };

        var (response, attemptCount) = await SendWithRetryAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("success", body);
        // First 2 attempts return 503, third returns 200
        Assert.Equal(3, attemptCount);
    }
}
